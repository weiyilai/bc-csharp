﻿using System;

using Org.BouncyCastle.Crypto.Utilities;

namespace Org.BouncyCastle.Bcpg.Sig
{
    internal static class Utilities
    {
        internal static bool BooleanFromBytes(byte[] bytes)
        {
            if (bytes.Length != 1)
                throw new InvalidOperationException("Byte array has unexpected length. Expected length 1, got " + bytes.Length);

            byte value = bytes[0];
            if (value > 1)
                throw new InvalidOperationException("Unexpected byte value for boolean encoding: " + value);

            return Convert.ToBoolean(value);
        }

        internal static byte[] BooleanToBytes(bool value) => new byte[1]{ Convert.ToByte(value) };

        internal static uint TimeFromBytes(byte[] bytes)
        {
            if (bytes.Length != 4)
                throw new InvalidOperationException("Byte array has unexpected length. Expected length 4, got " + bytes.Length);

            return Pack.BE_To_UInt32(bytes);
        }

        internal static byte[] TimeToBytes(uint t) => Pack.UInt32_To_BE(t);
    }
}
