using System;
using System.Threading;
using System.Collections.Generic;

namespace dotNSASM
{
    public partial class NSASM
    {
        protected class SafePool<T> : List<T>
        {
            private readonly object _lock = new object();

            public SafePool() : base()
            {
            }

            public new int Count
            {
                get
                {
                    try
                    {
                        Monitor.Enter(_lock);
                        return base.Count;
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
            }

            public new void Add(T value)
            {
                Monitor.Enter(_lock);
                base.Add(value);
                Monitor.Exit(_lock);
            }

            public new void Insert(int index, T value)
            {
                Monitor.Enter(_lock);
                base.Insert(index, value);
                Monitor.Exit(_lock);
            }

            public new T this[int index]
            {
                get
                {
                    try
                    {
                        Monitor.Enter(_lock);
                        return base[index];
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
            }
        }

        protected virtual void LoadFuncList()
        {
            funcList.Add("rem", (dst, src, ext) =>
            {
                return Result.OK;
            });

            funcList.Add("var", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("int", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("char", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.CHAR) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("float", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.FLOAT) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("str", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.STR) return Result.ERR;

                src.readOnly = true;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("code", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.CODE) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("map", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.VAR)) return Result.ERR;
                if (heapManager.ContainsKey((string)dst.data)) return Result.ERR;
                if (src.type != RegType.MAP) return Result.ERR;

                src.readOnly = false;
                heapManager.Add((string)dst.data, src);
                return Result.OK;
            });

            funcList.Add("mov", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (dst.type == RegType.CHAR && src.type == RegType.STR)
                {
                    dst.data = ((string)src.data)[src.strPtr];
                }
                else if (dst.type == RegType.STR && src.type == RegType.CHAR)
                {
                    char[] array = ((string)dst.data).ToCharArray();
                    array[dst.strPtr] = (char)src.data;
                    dst.data = new string(array);
                }
                else
                {
                    dst.Copy(src);
                    if (dst.readOnly) dst.readOnly = false;
                }
                return Result.OK;
            });

            funcList.Add("push", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (stackManager.Count >= stackSize) return Result.ERR;
                stackManager.Push(new Register(dst));
                return Result.OK;
            });

            funcList.Add("pop", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                dst.Copy(stackManager.Pop());
                return Result.OK;
            });

            funcList.Add("in", (dst, src, ext) =>
            {
                if (src == null)
                {
                    src = new Register();
                    src.type = RegType.INT;
                    src.data = 0x00;
                    src.readOnly = true;
                }
                if (dst == null) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;
                string buf; Register reg;
                switch ((int)src.data)
                {
                    case 0x00:
                        if (dst.readOnly && dst.type != RegType.STR) return Result.ERR;
                        buf = Util.Scan();
                        switch (dst.type)
                        {
                            case RegType.INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0];
                                break;
                            case RegType.FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.STR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf;
                                dst.strPtr = 0;
                                break;
                        }
                        break;
                    case 0xFF:
                        Util.Print("[DEBUG] <<< ");
                        if (dst.readOnly && dst.type != RegType.STR) return Result.ERR;
                        buf = Util.Scan();
                        switch (dst.type)
                        {
                            case RegType.INT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.INT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.CHAR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf[0];
                                break;
                            case RegType.FLOAT:
                                reg = GetRegister(buf);
                                if (reg == null) return Result.OK;
                                if (reg.type != RegType.FLOAT) return Result.OK;
                                dst.data = reg.data;
                                break;
                            case RegType.STR:
                                if (buf.Length < 1) return Result.OK;
                                dst.data = buf;
                                dst.strPtr = 0;
                                break;
                        }
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("out", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null)
                {
                    if (dst.type == RegType.STR)
                    {
                        Util.Print(((string)dst.data).Substring(dst.strPtr));
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        Register register = Eval(dst);
                        if (register == null) return Result.ERR;
                        Util.Print(register.data);
                    }
                    else Util.Print(dst.data);
                }
                else
                {
                    if (dst.type != RegType.INT)
                        return Result.ERR;
                    switch ((int)dst.data)
                    {
                        case 0x00:
                            if (src.type == RegType.STR)
                            {
                                Util.Print(((string)src.data).Substring(src.strPtr));
                            }
                            else if (src.type == RegType.CODE)
                            {
                                Register register = Eval(src);
                                if (register == null) return Result.ERR;
                                Util.Print(register.data);
                            }
                            else Util.Print(src.data);
                            break;
                        case 0xFF:
                            Util.Print("[DEBUG] >>> ");
                            if (src.type == RegType.STR)
                            {
                                Util.Print(((string)src.data).Substring(src.strPtr));
                            }
                            else if (src.type == RegType.CODE)
                            {
                                Register register = Eval(src);
                                if (register == null) return Result.ERR;
                                Util.Print(register.data);
                            }
                            else Util.Print(src.data);
                            Util.Print('\n');
                            break;
                        default:
                            return Result.ERR;
                    }
                }
                return Result.OK;
            });

            funcList.Add("prt", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src != null)
                {
                    if (ext != null)
                    {
                        Util.Print(
                            dst.data.ToString() +
                            src.data.ToString() +
                            ext.data.ToString() +
                            '\n'
                        );
                        return Result.OK;
                    }
                    if (dst.type == RegType.STR)
                    {
                        if (dst.readOnly) return Result.ERR;
                        if (src.type == RegType.CHAR && src.data.Equals('\b'))
                        {
                            if (dst.data.ToString().Contains("\n"))
                            {
                                string[] parts = dst.data.ToString().Split('\n');
                                string res = "";
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    res = res + parts[i];
                                    if (i < parts.Length - 2) res = res + "\n";
                                }
                                dst.data = res;
                            }
                        }
                        else if (src.type == RegType.CODE)
                        {
                            Register register = Eval(src);
                            if (register == null) return Result.ERR;
                            dst.data = dst.data.ToString() + '\n' + register.data.ToString();
                        }
                        else if (src.type == RegType.STR)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString().Substring(src.strPtr);
                        }
                        else return Result.ERR;
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        if (dst.readOnly) return Result.ERR;
                        if (src.type == RegType.CHAR && src.data.Equals('\b'))
                        {
                            if (dst.data.ToString().Contains("\n"))
                            {
                                string[] parts = dst.data.ToString().Split('\n');
                                string res = "";
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    res = res + parts[i];
                                    if (i < parts.Length - 2) res = res + "\n";
                                }
                                dst.data = res;
                            }
                        }
                        else if (src.type == RegType.CODE)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString();
                        }
                        else if (src.type == RegType.STR)
                        {
                            dst.data = dst.data.ToString() + '\n' + src.data.ToString().Substring(src.strPtr);
                        }
                        else return Result.ERR;
                    }
                    else return Result.ERR;
                }
                else
                {
                    if (dst == null) return Result.ERR;
                    if (dst.type == RegType.STR)
                    {
                        Util.Print(((string)dst.data).Substring(dst.strPtr) + '\n');
                    }
                    else if (dst.type == RegType.CODE)
                    {
                        Register register = Eval(dst);
                        if (register == null) return Result.ERR;
                        Util.Print(register.data.ToString() + '\n');
                    }
                    else Util.Print(dst.data.ToString() + '\n');
                }
                return Result.OK;
            });

            funcList.Add("add", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["add"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '+');
                else
                    return Calc(dst, src, '+');
            });

            funcList.Add("inc", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return Calc(dst, register, '+');
            });

            funcList.Add("sub", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["sub"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '-');
                else
                    return Calc(dst, src, '-');
            });

            funcList.Add("dec", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register register = new Register();
                register.readOnly = false;
                register.type = RegType.CHAR;
                register.data = 1;
                return Calc(dst, register, '-');
            });

            funcList.Add("mul", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mul"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '*');
                else
                    return Calc(dst, src, '*');
            });

            funcList.Add("div", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["div"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '/');
                else
                    return Calc(dst, src, '/');
            });

            funcList.Add("mod", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mod"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '%');
                else
                    return Calc(dst, src, '%');
            });

            funcList.Add("and", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["and"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '&');
                else
                    return Calc(dst, src, '&');
            });

            funcList.Add("or", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["or"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '|');
                else
                    return Calc(dst, src, '|');
            });

            funcList.Add("xor", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["xor"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '^');
                else
                    return Calc(dst, src, '^');
            });

            funcList.Add("not", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                return Calc(dst, null, '~');
            });

            funcList.Add("shl", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["shl"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '<');
                else
                    return Calc(dst, src, '<');
            });

            funcList.Add("shr", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["shr"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type == RegType.CODE)
                    return Calc(dst, Eval(src), '>');
                else
                    return Calc(dst, src, '>');
            });

            funcList.Add("cmp", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (funcList["mov"].Invoke(stateReg, dst, null) == Result.ERR)
                    return Result.ERR;
                if (src.type == RegType.CODE)
                {
                    if (funcList["sub"].Invoke(stateReg, Eval(src), null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["sub"].Invoke(stateReg, src, null) == Result.ERR)
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("test", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type == RegType.CODE)
                {
                    if (funcList["mov"].Invoke(stateReg, Eval(dst), null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["mov"].Invoke(stateReg, dst, null) == Result.ERR)
                        return Result.ERR;
                }

                Register reg = new Register();
                reg.type = dst.type; reg.readOnly = false; reg.data = 0;
                if (funcList["sub"].Invoke(stateReg, reg, null) == Result.ERR)
                    return Result.ERR;
                return Result.OK;
            });

            funcList.Add("jmp", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.TAG)) return Result.ERR;
                string tag = (string)dst.data;
                string segBuf, lineBuf;

                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);

                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (code[segBuf] == null) continue;
                    for (int line = 0; line < code[segBuf].Length; line++)
                    {
                        lineBuf = code[segBuf][line];
                        if (tag.Equals(lineBuf))
                        {
                            tmpSeg = seg;
                            tmpCnt = line;
                            return Result.OK;
                        }
                    }
                }

                return Result.ERR;
            });

            funcList.Add("jz", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) == 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jnz", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) != 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jg", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) > 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("jl", (dst, src, ext) =>
            {
                if ((float)ConvValue(stateReg.data, RegType.FLOAT) < 0)
                {
                    return funcList["jmp"].Invoke(dst, src, null);
                }
                return Result.OK;
            });

            funcList.Add("loop", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null) return Result.ERR;
                if (ext == null) return Result.ERR;

                if (dst.type != RegType.INT) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (src.type != RegType.INT) return Result.ERR;
                if (ext.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)ext.data, WordType.TAG)) return Result.ERR;

                if ((int)src.data > 0)
                {
                    if (funcList["inc"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                }
                else
                {
                    if (funcList["dec"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                }
                if (funcList["cmp"].Invoke(dst, src, null) == Result.ERR)
                    return Result.ERR;
                if (funcList["jnz"].Invoke(ext, null, null) == Result.ERR)
                    return Result.ERR;

                return Result.OK;
            });

            funcList.Add("end", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                    return Result.ETC;
                return Result.ERR;
            });

            funcList.Add("ret", (dst, src, ext) =>
            {
                if (src == null)
                {
                    if (dst != null) prevDstReg = dst;
                    else prevDstReg = regGroup[0];
                    return Result.ETC;
                }
                return Result.ERR;
            });

            funcList.Add("nop", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                    return Result.OK;
                return Result.ERR;
            });

            funcList.Add("rst", (dst, src, ext) =>
            {
                if (dst == null && src == null)
                {
                    tmpSeg = 0;
                    tmpCnt = 0;
                    return Result.OK;
                }
                return Result.ERR;
            });

            funcList.Add("run", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.SEG)) return Result.ERR;
                string segBuf, target = (string)dst.data;
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (target.Equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        return Result.OK;
                    }
                }
                return Result.ERR;
            });

            funcList.Add("call", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                if (!VerifyWord((string)dst.data, WordType.SEG)) return Result.ERR;
                string segBuf, target = (string)dst.data;
                string[] codeKeys = new string[code.Keys.Count];
                code.Keys.CopyTo(codeKeys, 0);
                for (int seg = 0; seg < codeKeys.Length; seg++)
                {
                    segBuf = codeKeys[seg];
                    if (target.Equals(segBuf))
                    {
                        tmpSeg = seg;
                        tmpCnt = 0;
                        backupReg.Push(progSeg);
                        backupReg.Push(progCnt);
                        return Result.OK;
                    }
                }
                return Result.OK;
            });

            funcList.Add("ld", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.type != RegType.STR && dst.type != RegType.CODE)
                    return Result.ERR;

                string path;
                if (dst.type == RegType.CODE)
                {
                    Register res = Eval(dst);
                    if (res == null) return Result.ERR;
                    if (res.type != RegType.STR) return Result.ERR;
                    path = res.data.ToString();
                }
                else path = dst.data.ToString();

                string code = Util.Read(path);
                if (code == null) return Result.ERR;
                string[][] segs = Util.GetSegments(code);
                if (AppendCode(segs) == Result.ERR)
                {
                    Util.Print("At file: " + path + "\n");
                    return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("eval", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;

                if (src == null) Eval(dst);
                else
                {
                    if (dst.readOnly) return Result.ERR;
                    dst.Copy(Eval(src));
                }

                return Result.OK;
            });

            funcList.Add("par", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (src == null) return Result.ERR;
                if (ext == null) return Result.ERR;

                if (dst.readOnly) return Result.ERR;
                if (src.type != RegType.CODE) return Result.ERR;
                if (ext.type != RegType.MAP) return Result.ERR;

                if (ext.data is Map map)
                {
                    if (map.Count != 0)
                    {
                        int cnt = map.Count;
                        string[][] code = Util.GetSegments(src.data.ToString());
                        List<Register> keys = new List<Register>(map.Keys);

                        Thread[] threads = new Thread[cnt];
                        SafePool<int> signPool = new SafePool<int>();
                        SafePool<NSASM> runnerPool = new SafePool<NSASM>();
                        SafePool<Register> outputPool = new SafePool<Register>();
                        for (int i = 0; i < cnt; i++)
                        {
                            NSASM core = Instance(this, code);
                            core.SetArgument(map[keys[i]]);
                            runnerPool.Add(core);
                            outputPool.Add(new Register());
                        }
                        for (int i = 0; i < cnt; i++)
                        {
                            threads[i] = new Thread(new ParameterizedThreadStart((arg) => {
                                if (arg is int index)
                                {
                                    NSASM core = runnerPool[index];
                                    outputPool.Insert(index, core.Run());
                                    signPool.Add(index);
                                }
                            }));
                        }

                        for (int i = 0; i < cnt; i++)
                            threads[i].Start(i);
                        while (signPool.Count < cnt)
                            funcList["nop"].Invoke(null, null, null);

                        dst.type = RegType.MAP;
                        dst.readOnly = false;
                        Map res = new Map();
                        for (int i = 0; i < cnt; i++)
                            res.Add(keys[i], outputPool[i]);
                        dst.data = res;
                    }
                }

                return Result.OK;
            });

            funcList.Add("use", (dst, src, ext) =>
            {
                if (src != null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (dst.type != RegType.MAP) return Result.ERR;
                useReg = dst;
                return Result.OK;
            });

            funcList.Add("put", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["use"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["put"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (useReg == null) return Result.ERR;
                if (useReg.type != RegType.MAP) return Result.ERR;
                if (dst.type == RegType.CODE)
                {
                    Register reg = Eval(dst);
                    if (reg == null) return Result.ERR;
                    if (!(reg.data is Map)) return Result.ERR;
                    if (((Map)useReg.data).ContainsKey(reg))
                        ((Map)useReg.data).Remove(reg);
                    ((Map)useReg.data).Add(new Register(reg), new Register(src));
                }
                else
                {
                    if (((Map)useReg.data).ContainsKey(dst))
                        ((Map)useReg.data).Remove(dst);
                    ((Map)useReg.data).Add(new Register(dst), new Register(src));
                }

                return Result.OK;
            });

            funcList.Add("get", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["use"].Invoke(dst, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["get"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                if (useReg == null) return Result.ERR;
                if (useReg.type != RegType.MAP) return Result.ERR;

                if (src.type == RegType.CODE)
                {
                    Register reg = Eval(src);
                    if (reg == null) return Result.ERR;
                    if (!(reg.data is Map)) return Result.ERR;
                    if (!((Map)useReg.data).ContainsKey(reg)) return Result.ERR;
                    return funcList["mov"](dst, ((Map)useReg.data)[reg], null);
                }
                else
                {
                    if (!((Map)useReg.data).ContainsKey(src)) return Result.ERR;
                    return funcList["mov"](dst, ((Map)useReg.data)[src], null);
                }
            });

            funcList.Add("cat", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["cat"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                switch (dst.type)
                {
                    case RegType.STR:
                        if (src.type != RegType.STR)
                            return Result.ERR;
                        dst.data = (string)dst.data + (string)src.data;
                        break;
                    case RegType.MAP:
                        if (src.type != RegType.MAP)
                            return Result.ERR;
                        if (!(dst.data is Map)) return Result.ERR;
                        if (!(src.data is Map)) return Result.ERR;
                        foreach (var i in (Map)src.data)
                        {
                            if (((Map)dst.data).ContainsKey(i.Key))
                                ((Map)dst.data).Remove(i.Key);
                            ((Map)dst.data).Add(i.Key, i.Value);
                        }
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("dog", (dst, src, ext) =>
            {
                if (ext != null)
                {
                    if (funcList["push"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["dog"].Invoke(src, ext, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["mov"].Invoke(dst, src, null) == Result.ERR)
                        return Result.ERR;
                    if (funcList["pop"].Invoke(src, null, null) == Result.ERR)
                        return Result.ERR;
                    return Result.OK;
                }
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                switch (dst.type)
                {
                    case RegType.STR:
                        if (src.type != RegType.STR)
                            return Result.ERR;
                        dst.data = ((string)dst.data).Replace((string)src.data, "");
                        break;
                    case RegType.MAP:
                        if (src.type != RegType.MAP)
                            return Result.ERR;
                        foreach (var i in (Map)src.data)
                            if (((Map)dst.data).ContainsKey(i.Key))
                                ((Map)dst.data).Remove(i.Key);
                        break;
                    default:
                        return Result.ERR;
                }
                return Result.OK;
            });

            funcList.Add("type", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;

                Register reg = new Register();
                reg.type = RegType.STR;
                reg.readOnly = true;
                switch (src.type)
                {
                    case RegType.INT: reg.data = "int"; break;
                    case RegType.CHAR: reg.data = "char"; break;
                    case RegType.FLOAT: reg.data = "float"; break;
                    case RegType.STR: reg.data = "str"; break;
                    case RegType.CODE: reg.data = "code"; break;
                    case RegType.MAP: reg.data = "map"; break;
                    case RegType.PAR: reg.data = "par"; break;
                    case RegType.NUL: reg.data = "nul"; break;
                }
                return funcList["mov"](dst, reg, null);
            });

            funcList.Add("len", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                if (dst.readOnly) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                if (src == null)
                {
                    if (useReg == null) return Result.ERR;
                    if (useReg.type != RegType.MAP) return Result.ERR;
                    if (!(useReg.data is Map)) return Result.ERR;
                    reg.data = ((Map)useReg.data).Count;
                }
                else
                {
                    if (src.type != RegType.STR) return Result.ERR;
                    reg.data = ((string)src.data).Length;
                }
                return funcList["mov"](dst, reg, null);
            });

            funcList.Add("ctn", (dst, src, ext) =>
            {
                if (dst == null) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                if (src == null)
                {
                    if (useReg == null) return Result.ERR;
                    if (useReg.type != RegType.MAP) return Result.ERR;
                    if (!(useReg.data is Map)) return Result.ERR;
                    reg.data = ((Map)useReg.data).ContainsKey(dst) ? 1 : 0;
                }
                else
                {
                    if (src.type != RegType.STR) return Result.ERR;
                    if (dst.type != RegType.STR) return Result.ERR;
                    reg.data = ((string)dst.data).Contains((string)src.data) ? 1 : 0;
                }
                return funcList["mov"](stateReg, reg, null);
            });

            funcList.Add("equ", (dst, src, ext) =>
            {
                if (src == null) return Result.ERR;
                if (dst == null) return Result.ERR;
                if (src.type != RegType.STR) return Result.ERR;
                if (dst.type != RegType.STR) return Result.ERR;
                Register reg = new Register();
                reg.type = RegType.INT;
                reg.readOnly = true;
                reg.data = ((string)dst.data).Equals((string)src.data) ? 0 : 1;
                return funcList["mov"](stateReg, reg, null);
            });
        }
    }
}
