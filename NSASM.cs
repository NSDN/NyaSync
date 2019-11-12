using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

namespace dotNSASM
{
    public partial class NSASM
    {
        public const string Version = "0.61 (.NET Standard 2.0)";

        public enum RegType
        {
            CHAR, STR, INT, FLOAT, CODE, MAP, PAR, NUL
        }

        public class Register
        {
            public RegType type;
            public object data;
            public int strPtr = 0;
            public bool readOnly;

            public override string ToString()
            {
                switch (type)
                {
                    case RegType.CODE:
                        return "(\n" + data.ToString() + "\n)";
                    default:
                        return data.ToString();
                }
            }

            public override bool Equals(object obj)
            {
                if (obj is Register)
                    return type.Equals(((Register)obj).type) && data.Equals(((Register)obj).data);
                return false;
            }

            public override int GetHashCode()
            {
                return data.GetHashCode();
            }

            public void Copy(Register reg)
            {
                type = reg.type;
                data = reg.data;
                strPtr = reg.strPtr;
                readOnly = reg.readOnly;
            }

            public Register()
            {
                type = RegType.NUL;
                data = 0;
                strPtr = 0;
                readOnly = false;
            }

            public Register(Register reg)
            {
                Copy(reg);
            }
        }

        public class Map : Dictionary<Register, Register>
        {
            public Map() : base() {}

            public override string ToString()
            {
                string str = "M(\n";
                foreach (Register key in Keys)
                {
                    if (this[key] is null) continue;
                    str += (key.ToString() + "->" + this[key].ToString() + "\n");
                }
                str += ")";

                return str;
            }
        }

        public delegate Result Operator(Register dst, Register src, Register ext);
        public delegate Register Param(Register reg); // if reg is null, it's read, else write

        private Dictionary<string, Register> heapManager;
        private Stack<Register> stackManager;
        private int heapSize, stackSize, regCnt;
        protected Register useReg;
        protected Register[] regGroup;
        private Register stateReg;
        private Register prevDstReg;

        private Register argReg;
        public void SetArgument(Register reg)
        {
            argReg = new Register(reg);
        }

        private Stack<int> backupReg;
        private int progSeg, tmpSeg;
        private int progCnt, tmpCnt;

        protected Dictionary<string, Operator> funcList;
        private Dictionary<string, string[]> code;

        protected Dictionary<string, Param> paramList;

        public enum Result
        {
            OK, ERR, ETC
        }

        private enum WordType
        {
            REG, CHAR, STR, INT,
            FLOAT, VAR, TAG, SEG,
            CODE, MAP, PAR
        }

        private bool VerifyBound(string var, char left, char right)
        {
            if (var.Length == 0) return false;
            return var[0] == left && var[var.Length - 1] == right;
        }

        private bool VerifyWord(string var, WordType type)
        {
            switch (type)
            {
                case WordType.REG:
                    return var[0] == 'r' || var[0] == 'R';
                case WordType.CHAR:
                    return VerifyBound(var, '\'', '\'');
                case WordType.STR:
                    return VerifyBound(var, '\"', '\"') ||
                           (var.Split('\"').Length > 2 && var.Contains("*"));
                case WordType.INT:
                    if (var.EndsWith("f") || var.EndsWith("F"))
                        return var.StartsWith("0x") || var.StartsWith("0X");
                    return (
                        !var.Contains(".")
                    ) && (
                        (var[0] >= '0' && var[0] <= '9') ||
                        var[0] == '-' || var[0] == '+' ||
                        var.EndsWith("h") || var.EndsWith("H")
                    );
                case WordType.FLOAT:
                    return (
                        var.Contains(".") ||
                        var.EndsWith("f") || var.EndsWith("F")
                    ) && (
                        (var[0] >= '0' && var[0] <= '9') ||
                        var[0] == '-' || var[0] == '+'
                    ) && (!var.StartsWith("0x") || !var.StartsWith("0X"));
                case WordType.TAG:
                    return VerifyBound(var, '[', ']');
                case WordType.SEG:
                    return VerifyBound(var, '<', '>');
                case WordType.CODE:
                    return VerifyBound(var, '(', ')');
                case WordType.MAP:
                    if (var[0] == 'm' || var[0] == 'M')
                        return VerifyBound(var.Substring(1), '(', ')');
                    else return false;
                case WordType.PAR:
                    return paramList.ContainsKey(var);
                case WordType.VAR:
                    return !VerifyWord(var, WordType.REG) && !VerifyWord(var, WordType.CHAR) &&
                           !VerifyWord(var, WordType.STR) && !VerifyWord(var, WordType.INT) &&
                           !VerifyWord(var, WordType.FLOAT) && !VerifyWord(var, WordType.TAG) &&
                           !VerifyWord(var, WordType.SEG) && !VerifyWord(var, WordType.CODE) &&
                           !VerifyWord(var, WordType.MAP) && !VerifyWord(var, WordType.PAR);
            }
            return false;
        }

        private Register GetRegister(string var)
        {
            if (var.Length == 0) return null;
            if (VerifyWord(var, WordType.PAR))
            {
                Register register = new Register();
                register.type = RegType.PAR;
                register.readOnly = true;
                register.data = var;
                return register;
            }
            else if (VerifyWord(var, WordType.REG))
            {
                //Register
                int index = int.Parse(var.Substring(1));
                if (index < 0 || index >= regGroup.Length) return null;
                return regGroup[index];
            }
            else if (VerifyWord(var, WordType.VAR))
            {
                //Variable
                if (!heapManager.ContainsKey(var)) return null;
                return heapManager[var];
            }
            else
            {
                //Immediate number
                Register register = new Register();
                if (VerifyWord(var, WordType.CHAR))
                {
                    if (var.Length < 3) return null;
                    char tmp = (char)0;
                    if (var[1] == '\\')
                    {
                        if (var.Length < 4) return null;
                        switch (var[2])
                        {
                            case '0': tmp = '\0'; break;
                            case 'b': tmp = '\b'; break;
                            case 'n': tmp = '\n'; break;
                            case 'r': tmp = '\r'; break;
                            case 't': tmp = '\t'; break;
                            case '\\': tmp = '\\'; break;
                        }
                    }
                    else
                    {
                        tmp = var[1];
                    }
                    register.type = RegType.CHAR;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.STR))
                {
                    if (var.Length < 3) return null;
                    string tmp, rep;
                    try
                    {
                        if (var.Contains("*"))
                        {
                            tmp = rep = var.Split(new string[]{ "\"*" }, StringSplitOptions.RemoveEmptyEntries)[0].Substring(1);
                            Register repeat = GetRegister(var.Split(new string[] { "\"*" }, StringSplitOptions.RemoveEmptyEntries)[1]);
                            if (repeat == null) return null;
                            if (repeat.type != RegType.INT) return null;
                            for (int i = 1; i < (int)repeat.data; i++)
                                tmp = tmp + rep;
                        }
                        else
                        {
                            tmp = var.Substring(1, var.Length - 2);
                        }
                    }
                    catch (Exception e)
                    {
                        return null;
                    }

                    tmp = Util.FormatString(tmp);

                    register.type = RegType.STR;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.INT))
                {
                    int tmp;
                    if (
                        (var.Contains("x") || var.Contains("X")) ^
                        (var.Contains("h") || var.Contains("H"))
                    )
                    {
                        if (
                            (var.Contains("x") || var.Contains("X")) &&
                            (var.Contains("h") || var.Contains("H"))
                        ) return null;
                        if (
                            (var[0] < '0' || var[0] > '9') &&
                            (var[0] != '+' || var[0] != '-')
                        ) return null;
                        try
                        {
                            tmp = int.Parse(
                                    var.Replace("h", "").Replace("H", "")
                                       .Replace("x", "").Replace("X", ""),
                                NumberStyles.HexNumber);
                        }
                        catch (Exception e)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        try
                        {
                            tmp = int.Parse(var);
                        }
                        catch (Exception e)
                        {
                            return null;
                        }
                    }
                    register.type = RegType.INT;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.FLOAT))
                {
                    float tmp;
                    try
                    {
                        tmp = float.Parse(var.Replace("f", "").Replace("F", ""));
                    }
                    catch (Exception e)
                    {
                        return null;
                    }
                    register.type = RegType.FLOAT;
                    register.readOnly = true;
                    register.data = tmp;
                }
                else if (VerifyWord(var, WordType.TAG) || VerifyWord(var, WordType.SEG))
                {
                    register.type = RegType.STR;
                    register.readOnly = true;
                    register.data = var;
                }
                else if (VerifyWord(var, WordType.CODE))
                {
                    register.type = RegType.CODE;
                    register.readOnly = true;
                    string code = var.Substring(1, var.Length - 2);
                    code = Util.DecodeLambda(code);
                    register.data = code;
                }
                else if (VerifyWord(var, WordType.MAP))
                {
                    string code = var.Substring(2, var.Length - 3);

                    register = new Register();
                    register.type = RegType.MAP;
                    register.readOnly = true;
                    register.data = new Map();
                    code = Util.DecodeLambda(code);
                    funcList["mov"].Invoke(regGroup[regCnt], register, null);

                    Register reg = new Register();
                    reg.type = RegType.CODE; reg.readOnly = true;
                    reg.data = code + "\n" + "ret r" + regCnt + "\n";
                    register = Eval(reg);
                }
                else return null;
                return register;
            }
        }

        public Result Execute(string var)
        {
            string op, dst, src, ext;
            Register dr = null, sr = null, er = null;

            op = var.Split(' ')[0];
            op = op.ToLower(); //To lower case
            if (op.Length + 1 < var.Length)
            {
                if (
                        op.Equals("var") || op.Equals("int") ||
                        op.Equals("char") || op.Equals("float") ||
                        op.Equals("str") || op.Equals("code") ||
                        op.Equals("map")
                    )
                {
                    //Variable define
                    dst = var.Substring(op.Length + 1).Split('=')[0];
                    if (var.Length <= op.Length + 1 + dst.Length) return Result.ERR;
                    if (var[op.Length + 1 + dst.Length] == '=')
                        src = var.Substring(op.Length + 1 + dst.Length + 1);
                    else src = "";
                    dr = new Register();
                    dr.readOnly = true; dr.type = RegType.STR; dr.data = dst;
                    sr = GetRegister(src);
                }
                else if (op == "rem")
                {
                    //Comment
                    return Result.OK;
                }
                else
                {
                    //Normal code
                    string regs = var.Substring(op.Length + 1), res = "";
                    var strings = Util.GetStrings(regs, out res);
                    var args = Util.ParseArgs(res, ',');
                    for (int i = 0; i < args.Count; i++)
                        foreach (var it in strings)
                            args[i] = args[i].Replace(it.Key, it.Value);

                    dst = src = ext = "";
                    if (args.Count > 0) dst = args[0];
                    if (args.Count > 1) src = args[1];
                    if (args.Count > 2) ext = args[2];

                    dr = GetRegister(dst);
                    sr = GetRegister(src);
                    er = GetRegister(ext);
                }
            }

            if (!funcList.ContainsKey(op))
                return VerifyWord(op, WordType.TAG) ? Result.OK : Result.ERR;

            Register tdr = null, tsr = null, ter = null;
            string pdr = "", psr = "", per = "";
            if (dr != null && dr.type == RegType.PAR)
            {
                pdr = (string)dr.data;
                tdr = paramList[pdr].Invoke(null);
                dr = new Register(tdr);
            }
            if (sr != null && sr.type == RegType.PAR)
            {
                psr = (string)sr.data;
                tsr = paramList[psr].Invoke(null);
                sr = new Register(tsr);
            }
            if (er != null && er.type == RegType.PAR)
            {
                per = (string)er.data;
                ter = paramList[per].Invoke(null);
                er = new Register(ter);
            }

            prevDstReg = dr != null ? dr : prevDstReg;
            Result result = funcList[op].Invoke(dr, sr, er);

            if (ter != null && !ter.Equals(er))
                paramList[per].Invoke(er);
            if (tsr != null && !tsr.Equals(sr))
                paramList[psr].Invoke(sr);
            if (tdr != null && !tdr.Equals(dr))
                paramList[pdr].Invoke(dr);

            return result;
        }

        public Register Run()
        {
            if (code == null) return null;
            Result result; string segBuf, codeBuf;

            progSeg = progCnt = 0;

            for (; progSeg < code.Keys.Count; progSeg++)
            {
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                segBuf = codeKeys[progSeg];
                if (code[segBuf] == null) continue;

                for (; progCnt < code[segBuf].Length; progCnt++)
                {
                    if (tmpSeg >= 0 || tmpCnt >= 0)
                    {
                        progSeg = tmpSeg; progCnt = tmpCnt;
                        tmpSeg = -1; tmpCnt = -1;
                    }

                    segBuf = codeKeys[progSeg];
                    if (code[segBuf] == null) break;
                    codeBuf = code[segBuf][progCnt];

                    if (codeBuf.Length == 0)
                    {
                        continue;
                    }

                    result = Execute(codeBuf);
                    if (result == Result.ERR)
                    {
                        Util.Print("\nNSASM running error!\n");
                        Util.Print("At " + segBuf + ", line " + (progCnt + 1) + ": " + codeBuf + "\n\n");
                        return null;
                    }
                    else if (result == Result.ETC)
                    {
                        if (prevDstReg != null) prevDstReg.readOnly = false;
                        return prevDstReg;
                    }
                }

                if (backupReg.Count > 0)
                {
                    progCnt = backupReg.Pop() + 1;
                    progSeg = backupReg.Pop() - 1;
                }
                else progCnt = 0;
            }

            if (prevDstReg != null) prevDstReg.readOnly = false;
            return prevDstReg;
        }

        public void Call(string segName)
        {
            Result result; string segBuf, codeBuf;
            string[] codeKeys = new string[code.Keys.Count];

            code.Keys.CopyTo(codeKeys, 0);
            for (int seg = 0; seg < codeKeys.Length; seg++)
            {
                segBuf = codeKeys[seg];
                if (segName.Equals(segBuf))
                {
                    tmpSeg = seg;
                    tmpCnt = 0;
                    break;
                }
            }

            for (; progSeg < code.Keys.Count; progSeg++)
            {
                codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                segBuf = codeKeys[progSeg];
                if (code[segBuf] == null) continue;

                for (; progCnt < code[segBuf].Length; progCnt++)
                {
                    if (tmpSeg >= 0 || tmpCnt >= 0)
                    {
                        progSeg = tmpSeg; progCnt = tmpCnt;
                        tmpSeg = -1; tmpCnt = -1;
                    }

                    segBuf = codeKeys[progSeg];
                    if (code[segBuf] == null) break;
                    codeBuf = code[segBuf][progCnt];

                    if (codeBuf.Length == 0)
                    {
                        continue;
                    }

                    result = Execute(codeBuf);
                    if (result == Result.ERR)
                    {
                        Util.Print("\nNSASM running error!\n");
                        Util.Print("At " + segBuf + ", line " + (progCnt + 1) + ": " + codeBuf + "\n\n");
                        return;
                    }
                    else if (result == Result.ETC)
                    {
                        return;
                    }
                }

                if (backupReg.Count > 0)
                {
                    progCnt = backupReg.Pop() + 1;
                    progSeg = backupReg.Pop() - 1;
                }
                else progCnt = 0;
            }
        }

        protected virtual NSASM Instance(NSASM super, string[][] code)
        {
            return new NSASM(super, code);
        }

        protected Register Eval(Register register)
        {
            if (register == null) return null;
            if (register.type != RegType.CODE) return null;
            string[][] code = Util.GetSegments(register.data.ToString());
            return Instance(this, code).Run();
        }

        private string[] ConvToArray(string var)
        {
            StringReader reader = new StringReader(var);
            LinkedList<string> buf = new LinkedList<string>();

            while (reader.Peek() != -1)
            {
                buf.AddLast(reader.ReadLine());
            }

            if (buf.Count == 0) return null;

            string[] array = new string[buf.Count];
            buf.CopyTo(array, 0);

            reader.Dispose();
            return array;
        }

        private Result AppendCode(string[][] code)
        {
            if (code == null) return Result.OK;
            foreach (string[] seg in code)
            {
                if (seg[0].StartsWith(".")) continue; //This is conf seg
                if (seg[0].StartsWith("@")) //This is override seg
                {
                    if (!this.code.ContainsKey(seg[0].Substring(1)))
                    {
                        Util.Print("\nNSASM loading error!\n");
                        Util.Print("At " + seg[0].Substring(1) + "\n");
                        return Result.ERR;
                    }
                    this.code.Remove(seg[0].Substring(1));
                    this.code.Add(seg[0].Substring(1), ConvToArray(seg[1]));
                }
                else
                {
                    if (this.code.ContainsKey(seg[0]))
                    {
                        if (seg[0].StartsWith("_pub_")) continue; //This is pub seg
                        Util.Print("\nNSASM loading error!\n");
                        Util.Print("At " + seg[0] + "\n");
                        return Result.ERR;
                    }
                    this.code.Add(seg[0], ConvToArray(seg[1]));
                }
            }
            return Result.OK;
        }

        private void CopyRegGroup(NSASM super)
        {
            for (int i = 0; i < super.regGroup.Length; i++)
                this.regGroup[i].Copy(super.regGroup[i]);
        }

        private NSASM(NSASM super, string[][] code) : this(super.heapSize, super.stackSize, super.regCnt, code)
        {
            CopyRegGroup(super);
        }

        public NSASM(int heapSize, int stackSize, int regCnt, string[][] code)
        {
            heapManager = new Dictionary<string, Register>(heapSize);
            stackManager = new Stack<Register>();
            this.heapSize = heapSize;
            this.stackSize = stackSize;
            this.regCnt = regCnt;

            stateReg = new Register();
            stateReg.data = 0;
            stateReg.readOnly = false;
            stateReg.type = RegType.INT;

            backupReg = new Stack<int>();
            progSeg = 0; progCnt = 0;
            tmpSeg = -1; tmpCnt = -1;

            regGroup = new Register[regCnt + 1];
            for (int i = 0; i < regGroup.Length; i++)
            {
                regGroup[i] = new Register();
                regGroup[i].type = RegType.INT;
                regGroup[i].readOnly = false;
                regGroup[i].data = 0;
            }
            useReg = regGroup[regCnt];
            argReg = null;

            funcList = new Dictionary<string, Operator>();
            LoadFuncList();

            paramList = new Dictionary<string, Param>();
            LoadParamList();

            this.code = new Dictionary<string, string[]>();
            if (AppendCode(code) == Result.ERR)
            {
                Util.Print("At file: " + "_main_" + "\n\n");
                this.code.Clear();
            }
        }

        private object ConvValue(object value, RegType type)
        {
            switch (type)
            {
                case RegType.INT:
                    return int.Parse(value.ToString());
                case RegType.CHAR:
                    return (value.ToString())[0];
                case RegType.FLOAT:
                    return float.Parse(value.ToString());
            }
            return value;
        }

        private Result CalcInt(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (int)ConvValue(dst.data, RegType.INT) + (int)ConvValue(src.data, RegType.INT); break;
                case '-': dst.data = (int)ConvValue(dst.data, RegType.INT) - (int)ConvValue(src.data, RegType.INT); break;
                case '*': dst.data = (int)ConvValue(dst.data, RegType.INT) * (int)ConvValue(src.data, RegType.INT); break;
                case '/': dst.data = (int)ConvValue(dst.data, RegType.INT) / (int)ConvValue(src.data, RegType.INT); break;
                case '%': dst.data = (int)ConvValue(dst.data, RegType.INT) % (int)ConvValue(src.data, RegType.INT); break;
                case '&': dst.data = (int)ConvValue(dst.data, RegType.INT) & (int)ConvValue(src.data, RegType.INT); break;
                case '|': dst.data = (int)ConvValue(dst.data, RegType.INT) | (int)ConvValue(src.data, RegType.INT); break;
                case '~': dst.data = ~(int)ConvValue(dst.data, RegType.INT); break;
                case '^': dst.data = (int)ConvValue(dst.data, RegType.INT) ^ (int)ConvValue(src.data, RegType.INT); break;
                case '<': dst.data = (int)ConvValue(dst.data, RegType.INT) << (int)ConvValue(src.data, RegType.INT); break;
                case '>': dst.data = (int)ConvValue(dst.data, RegType.INT) >> (int)ConvValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcChar(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (char)ConvValue(dst.data, RegType.CHAR) + (char)ConvValue(src.data, RegType.CHAR); break;
                case '-': dst.data = (char)ConvValue(dst.data, RegType.CHAR) - (char)ConvValue(src.data, RegType.CHAR); break;
                case '*': dst.data = (char)ConvValue(dst.data, RegType.CHAR) * (char)ConvValue(src.data, RegType.CHAR); break;
                case '/': dst.data = (char)ConvValue(dst.data, RegType.CHAR) / (char)ConvValue(src.data, RegType.CHAR); break;
                case '%': dst.data = (char)ConvValue(dst.data, RegType.CHAR) % (char)ConvValue(src.data, RegType.CHAR); break;
                case '&': dst.data = (char)ConvValue(dst.data, RegType.CHAR) & (char)ConvValue(src.data, RegType.CHAR); break;
                case '|': dst.data = (char)ConvValue(dst.data, RegType.CHAR) | (char)ConvValue(src.data, RegType.CHAR); break;
                case '~': dst.data = ~(char)ConvValue(dst.data, RegType.CHAR); break;
                case '^': dst.data = (char)ConvValue(dst.data, RegType.CHAR) ^ (char)ConvValue(src.data, RegType.CHAR); break;
                case '<': dst.data = (char)ConvValue(dst.data, RegType.CHAR) << (char)ConvValue(src.data, RegType.CHAR); break;
                case '>': dst.data = (char)ConvValue(dst.data, RegType.CHAR) >> (char)ConvValue(src.data, RegType.CHAR); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcFloat(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) + (float)ConvValue(src.data, RegType.FLOAT); break;
                case '-': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) - (float)ConvValue(src.data, RegType.FLOAT); break;
                case '*': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) * (float)ConvValue(src.data, RegType.FLOAT); break;
                case '/': dst.data = (float)ConvValue(dst.data, RegType.FLOAT) / (float)ConvValue(src.data, RegType.FLOAT); break;
                default: return Result.ERR;
            }
            return Result.OK;
        }

        private Result CalcStr(Register dst, Register src, char type)
        {
            switch (type)
            {
                case '+': dst.strPtr = dst.strPtr + (int)ConvValue(src.data, RegType.INT); break;
                case '-': dst.strPtr = dst.strPtr - (int)ConvValue(src.data, RegType.INT); break;
                default: return Result.ERR;
            }
            if (dst.strPtr >= dst.data.ToString().Length) dst.strPtr = dst.data.ToString().Length - 1;
            if (dst.strPtr < 0) dst.strPtr = 0;
            return Result.OK;
        }

        private Result Calc(Register dst, Register src, char type)
        {
            switch (dst.type)
            {
                case RegType.INT:
                    return CalcInt(dst, src, type);
                case RegType.CHAR:
                    return CalcChar(dst, src, type);
                case RegType.FLOAT:
                    return CalcFloat(dst, src, type);
                case RegType.STR:
                    return CalcStr(dst, src, type);
            }
            return Result.OK;
        }

    }
}
