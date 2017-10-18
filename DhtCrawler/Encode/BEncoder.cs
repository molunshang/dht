﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DhtCrawler
{
    public class BEncoder
    {
        private static class Flags
        {

            public const byte Number = (byte)'i';
            public const byte List = (byte)'l';
            public const byte Dictionary = (byte)'d';
            public const byte End = (byte)'e';
            public const byte Split = (byte)':';
            public const byte String0 = (byte)'0';
            public const byte String1 = (byte)'1';
            public const byte String2 = (byte)'2';
            public const byte String3 = (byte)'3';
            public const byte String4 = (byte)'4';
            public const byte String5 = (byte)'5';
            public const byte String6 = (byte)'6';
            public const byte String7 = (byte)'7';
            public const byte String8 = (byte)'8';
            public const byte String9 = (byte)'9';

        }

        private static string EncodeObject(object item)
        {
            if (item is string)
            {
                return EncodeString((string)item);
            }
            else if (item is long)
            {
                return EncodeNumber((long)item);
            }
            else if (item is IList<object>)
            {
                return (EncodeList((IList<object>)item));
            }
            else if (item is IDictionary<string, object>)
            {
                return (EncodeDictionary((IDictionary<string, object>)item));
            }
            else
            {
                throw new ArgumentException("the type must be string,number,list or dictionary");
            }
        }

        public static string EncodeString(string str)
        {
            var length = Encoding.ASCII.GetByteCount(str);
            return length + ":" + str;
        }

        public static string EncodeNumber(long number)
        {
            return "i" + number + "e";
        }

        public static string EncodeList(IList<object> list)
        {
            var sb = new StringBuilder("l");
            foreach (var item in list)
            {
                sb.Append(EncodeObject(item));
            }
            return sb.Append("e").ToString();
        }

        public static string EncodeDictionary(IDictionary<string, object> dictionary)
        {
            var sb = new StringBuilder("d");
            foreach (var kv in dictionary)
            {
                sb.Append(EncodeString(kv.Key)).Append(EncodeObject(kv.Value));
            }
            return sb.Append("e").ToString();
        }

        public static object Decode(byte[] data)
        {
            var index = 0;
            return Decode(data, ref index);
        }

        public static object Decode(byte[] data, ref int index)
        {
            switch (data[index])
            {
                case Flags.Number:
                    index++;
                    var number = new StringBuilder();
                    for (; index < data.Length; index++)
                    {
                        if (data[index] == Flags.End)
                            break;
                        number.Append((char)data[index]);
                    }
                    index++;
                    return long.Parse(number.ToString());
                case Flags.String0:
                case Flags.String1:
                case Flags.String2:
                case Flags.String3:
                case Flags.String4:
                case Flags.String5:
                case Flags.String6:
                case Flags.String7:
                case Flags.String8:
                case Flags.String9:
                    var length = new StringBuilder();
                    for (; index < data.Length; index++)
                    {
                        if (data[index] == Flags.Split)
                            break;
                        length.Append((char)data[index]);
                    }
                    var startIndex = index + 1;
                    var strlength = int.Parse(length.ToString());
                    index = startIndex + strlength;
                    var strBytes = new byte[strlength];
                    Array.Copy(data, startIndex, strBytes, 0, strlength);
                    return Encoding.ASCII.GetString(strBytes);
                case Flags.List:
                    index++;
                    var list = new List<object>();
                    while (index < data.Length)
                    {
                        list.Add(Decode(data, ref index));
                        if (data[index] == Flags.End)
                            break;
                    }
                    index++;
                    return list;
                case Flags.Dictionary:
                    index++;
                    var dic = new Dictionary<string, object>();
                    while (index < data.Length)
                    {
                        var key = (string)Decode(data, ref index);
                        var value = Decode(data, ref index);
                        dic.Add(key, value);
                        if (data[index] == Flags.End)
                            break;
                    }
                    index++;
                    return dic;
                default:
                    throw new ArgumentException("unknown type flag,cann't decode");
            }
        }
    }
}
