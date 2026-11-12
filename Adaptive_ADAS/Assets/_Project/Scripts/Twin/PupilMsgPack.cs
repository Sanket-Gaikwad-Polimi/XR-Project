// PupilMsgPack.cs
// A small msgpack reader and writer, only what the Pupil Capture messages need.
// msgpack is the binary format Pupil Capture uses for the payload of its network messages
// (checked in the Pupil source code, zmq_tools.py: msgpack.packb(payload, use_bin_type=True)).
//
// Why our own instead of the MessagePack-CSharp library used by Pupil's hmd-eyes: that library
// is large (about 100 source files) and, according to the hmd-eyes README, needs Unity's API
// Compatibility Level ".NET 4.x" because of an issue with it. Pupil's messages only need the basic
// types, which take about 200 lines. Specification: https://github.com/msgpack/msgpack/blob/master/spec.md
//
// Read result (plain C# objects, easy to inspect):
//   map -> Dictionary<string, object> (keys turned into strings), array -> List<object>,
//   integers -> long, floats -> double, str -> string, bin and ext -> byte[], bool, nil -> null.
// The writer is used only by the fake Pupil stream (FakePupilServer).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AdaptiveADAS.Twin
{
    public static class PupilMsgPack
    {
        // ------------------------------------------------------------------ reading

        /// <summary>Decodes one msgpack value. Throws FormatException on broken data.</summary>
        public static object Decode(byte[] data)
        {
            int pos = 0;
            object v = Read(data, ref pos);
            return v;
        }

        private static object Read(byte[] b, ref int p)
        {
            byte t = Byte(b, ref p);
            if (t <= 0x7f) return (long)t;                                  // positive fixint
            if (t >= 0xe0) return (long)(sbyte)t;                           // negative fixint
            if ((t & 0xf0) == 0x80) return ReadMap(b, ref p, t & 0x0f);     // fixmap
            if ((t & 0xf0) == 0x90) return ReadArray(b, ref p, t & 0x0f);   // fixarray
            if ((t & 0xe0) == 0xa0) return Str(b, ref p, t & 0x1f);         // fixstr
            switch (t)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xc4: return Bytes(b, ref p, Byte(b, ref p));          // bin 8
                case 0xc5: return Bytes(b, ref p, (int)BigEndian(b, ref p, 2));
                case 0xc6: return Bytes(b, ref p, checked((int)BigEndian(b, ref p, 4)));
                case 0xc7: { int n = Byte(b, ref p); p++; return Bytes(b, ref p, n); }                       // ext 8 (type skipped)
                case 0xc8: { int n = (int)BigEndian(b, ref p, 2); p++; return Bytes(b, ref p, n); }
                case 0xc9: { int n = checked((int)BigEndian(b, ref p, 4)); p++; return Bytes(b, ref p, n); }
                case 0xca: return (double)Float32((uint)BigEndian(b, ref p, 4));                          // float 32
                case 0xcb: return BitConverter.Int64BitsToDouble((long)BigEndian(b, ref p, 8));             // float 64
                case 0xcc: return (long)BigEndian(b, ref p, 1);                                             // uint 8..64
                case 0xcd: return (long)BigEndian(b, ref p, 2);
                case 0xce: return (long)BigEndian(b, ref p, 4);
                case 0xcf: return unchecked((long)BigEndian(b, ref p, 8));
                case 0xd0: return (long)(sbyte)BigEndian(b, ref p, 1);                                      // int 8..64
                case 0xd1: return (long)(short)BigEndian(b, ref p, 2);
                case 0xd2: return (long)(int)BigEndian(b, ref p, 4);
                case 0xd3: return unchecked((long)BigEndian(b, ref p, 8));
                case 0xd4: p++; return Bytes(b, ref p, 1);                                                  // fixext 1..16 (type skipped)
                case 0xd5: p++; return Bytes(b, ref p, 2);
                case 0xd6: p++; return Bytes(b, ref p, 4);
                case 0xd7: p++; return Bytes(b, ref p, 8);
                case 0xd8: p++; return Bytes(b, ref p, 16);
                case 0xd9: return Str(b, ref p, Byte(b, ref p));                                            // str 8..32
                case 0xda: return Str(b, ref p, (int)BigEndian(b, ref p, 2));
                case 0xdb: return Str(b, ref p, checked((int)BigEndian(b, ref p, 4)));
                case 0xdc: return ReadArray(b, ref p, (int)BigEndian(b, ref p, 2));                         // array 16 / 32
                case 0xdd: return ReadArray(b, ref p, checked((int)BigEndian(b, ref p, 4)));
                case 0xde: return ReadMap(b, ref p, (int)BigEndian(b, ref p, 2));                           // map 16 / 32
                case 0xdf: return ReadMap(b, ref p, checked((int)BigEndian(b, ref p, 4)));
                default: throw new FormatException($"msgpack: unknown type byte 0x{t:x2} at {p - 1}");
            }
        }

        private static Dictionary<string, object> ReadMap(byte[] b, ref int p, int n)
        {
            var d = new Dictionary<string, object>(n);
            for (int i = 0; i < n; i++)
            {
                object key = Read(b, ref p);
                object val = Read(b, ref p);
                string k = key switch
                {
                    string s => s,
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    null => "",
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => key.ToString(),
                };
                d[k] = val;
            }
            return d;
        }

        private static List<object> ReadArray(byte[] b, ref int p, int n)
        {
            var list = new List<object>(n);
            for (int i = 0; i < n; i++) list.Add(Read(b, ref p));
            return list;
        }

        private static byte Byte(byte[] b, ref int p)
        {
            if (p >= b.Length) throw new FormatException("msgpack: unexpected end of data");
            return b[p++];
        }

        private static ulong BigEndian(byte[] b, ref int p, int n)
        {
            if (p + n > b.Length) throw new FormatException("msgpack: unexpected end of data");
            ulong v = 0;
            for (int i = 0; i < n; i++) v = (v << 8) | b[p++];
            return v;
        }

        // BitConverter.Int32BitsToSingle is missing in the .NET Framework API level, so via bytes.
        private static float Float32(uint bits)
        {
            byte[] four = BitConverter.GetBytes(bits);    // machine byte order, same as ToSingle expects
            return BitConverter.ToSingle(four, 0);
        }

        private static string Str(byte[] b, ref int p, int n)
        {
            if (n < 0 || p + n > b.Length) throw new FormatException("msgpack: string past end of data");
            string s = Encoding.UTF8.GetString(b, p, n);
            p += n;
            return s;
        }

        private static byte[] Bytes(byte[] b, ref int p, int n)
        {
            if (n < 0 || p + n > b.Length) throw new FormatException("msgpack: bytes past end of data");
            var r = new byte[n];
            Buffer.BlockCopy(b, p, r, 0, n);
            p += n;
            return r;
        }

        // ------------------------------------------------------------------ helpers for decoded data

        public static double GetDouble(Dictionary<string, object> d, string key, double fallback = double.NaN) =>
            d != null && d.TryGetValue(key, out object v) ? ToDouble(v, fallback) : fallback;

        public static double ToDouble(object v, double fallback = double.NaN) => v switch
        {
            double x => x,
            long x => x,
            bool x => x ? 1.0 : 0.0,
            _ => fallback,
        };

        public static string GetString(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out object v) && v != null ? (v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture)) : "";

        public static bool GetBool(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out object v) && (v is bool x ? x : ToDouble(v, 0) != 0);

        /// <summary>Two numbers [x, y] (e.g. norm_pos). False if missing.</summary>
        public static bool GetXY(Dictionary<string, object> d, string key, out double x, out double y)
        {
            x = y = double.NaN;
            if (d == null || !d.TryGetValue(key, out object v) || !(v is List<object> l) || l.Count < 2) return false;
            x = ToDouble(l[0]); y = ToDouble(l[1]);
            return !double.IsNaN(x) && !double.IsNaN(y);
        }

        // ------------------------------------------------------------------ writing (fake stream only)

        /// <summary>Encodes a value made of dictionaries (string keys), lists/arrays, strings,
        /// numbers, bools and null.</summary>
        public static byte[] Encode(object value)
        {
            using var ms = new MemoryStream(256);
            Write(ms, value);
            return ms.ToArray();
        }

        private static void Write(MemoryStream s, object v)
        {
            switch (v)
            {
                case null: s.WriteByte(0xc0); break;
                case bool x: s.WriteByte(x ? (byte)0xc3 : (byte)0xc2); break;
                case string x: WriteStr(s, x); break;
                case double x: s.WriteByte(0xcb); WriteBig(s, (ulong)BitConverter.DoubleToInt64Bits(x), 8); break;
                case float x: s.WriteByte(0xcb); WriteBig(s, (ulong)BitConverter.DoubleToInt64Bits(x), 8); break;
                case int x: WriteInt(s, x); break;
                case long x: WriteInt(s, x); break;
                case IDictionary<string, object> m:
                    WriteHeader(s, m.Count, 0x80, 0xde, 0xdf);
                    foreach (KeyValuePair<string, object> kv in m) { WriteStr(s, kv.Key); Write(s, kv.Value); }
                    break;
                case IList list:
                    WriteHeader(s, list.Count, 0x90, 0xdc, 0xdd);
                    foreach (object item in list) Write(s, item);
                    break;
                default: throw new ArgumentException("msgpack: cannot write " + v.GetType().Name);
            }
        }

        private static void WriteInt(MemoryStream s, long x)
        {
            if (x >= 0 && x <= 0x7f) { s.WriteByte((byte)x); return; }
            if (x < 0 && x >= -32) { s.WriteByte(unchecked((byte)(sbyte)x)); return; }
            s.WriteByte(0xd3); WriteBig(s, unchecked((ulong)x), 8);
        }

        private static void WriteStr(MemoryStream s, string x)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(x);
            int n = utf8.Length;
            if (n <= 31) s.WriteByte((byte)(0xa0 | n));
            else if (n <= 0xff) { s.WriteByte(0xd9); s.WriteByte((byte)n); }
            else if (n <= 0xffff) { s.WriteByte(0xda); WriteBig(s, (ulong)n, 2); }
            else { s.WriteByte(0xdb); WriteBig(s, (ulong)n, 4); }
            s.Write(utf8, 0, n);
        }

        private static void WriteHeader(MemoryStream s, int n, int fixBase, byte code16, byte code32)
        {
            if (n <= 15) s.WriteByte((byte)(fixBase | n));
            else if (n <= 0xffff) { s.WriteByte(code16); WriteBig(s, (ulong)n, 2); }
            else { s.WriteByte(code32); WriteBig(s, (ulong)n, 4); }
        }

        private static void WriteBig(MemoryStream s, ulong v, int n)
        {
            for (int i = n - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
        }
    }
}
