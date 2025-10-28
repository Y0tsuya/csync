using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class Header {

	///////////////////// ARRAY /////////////////////

	public static byte[] Copy(byte[] source, int index, int len) {
		byte[] rval = new byte[len];
		Array.Copy(source, index, rval, 0, len);
		return rval;
	}

	public static void Paste(byte[] source, byte[] dest, int index) {
		Array.Copy(source, 0, dest, index, source.Length);
	}

	public static byte[] Copy(byte[] source, ref int index, int len) {
		byte[] rval = Copy(source, index, len);
		index += len;
		return rval;
	}

	public static void Paste(byte[] source, byte[] dest, ref int index) {
		Array.Copy(source, 0, dest, index, source.Length);
		index += source.Length;
	}

	///////////////////// SHORT /////////////////////

	public static void ShortToByteArray(short inval, byte[] bytearray, int index) {
		bytearray[index] = (byte)inval;
		bytearray[index + 1] = (byte)(inval >> 8);
	}

	public static void ShortToByteArray(ushort inval, byte[] bytearray, int index) {
		bytearray[index] = (byte)inval;
		bytearray[index + 1] = (byte)(inval >> 8);
	}

	public static short ByteArrayToShort(byte[] bytearray, int index) {
		short rval;
		rval = (short)(bytearray[index] | (bytearray[index + 1] << 8));
		return rval;
	}

	public static ushort ByteArrayToUShort(byte[] bytearray, int index) {
		ushort rval;
		rval = (ushort)(bytearray[index] | (bytearray[index + 1] << 8));
		return rval;
	}

	public static void ShortToByteArray(short inval, byte[] bytearray, ref int index) {
		ShortToByteArray(inval, bytearray, index);
		index += 2;
	}

	public static void ShortToByteArray(ushort inval, byte[] bytearray, ref int index) {
		ShortToByteArray(inval, bytearray, index);
		index += 2;
	}

	public static short ByteArrayToShort(byte[] bytearray, ref int index) {
		short rval = ByteArrayToShort(bytearray, index);
		index += 2;
		return rval;
	}

	public static ushort ByteArrayToUShort(byte[] bytearray, ref int index) {
		ushort rval = ByteArrayToUShort(bytearray, index);
		index += 2;
		return rval;
	}

	///////////////////// INT /////////////////////

	public static void IntToByteArray(uint inval, byte[] bytearray, int index) {
		bytearray[index] = (byte)inval;
		bytearray[index + 1] = (byte)(inval >> 8);
		bytearray[index + 2] = (byte)(inval >> 16);
		bytearray[index + 3] = (byte)(inval >> 24);
	}

	public static void IntToByteArray(int inval, byte[] bytearray, int index) {
		bytearray[index] = (byte)inval;
		bytearray[index + 1] = (byte)(inval >> 8);
		bytearray[index + 2] = (byte)(inval >> 16);
		bytearray[index + 3] = (byte)(inval >> 24);
	}

	public static int ByteArrayToInt(byte[] bytearray, int index) {
		int rval;
		rval = bytearray[index] | (bytearray[index + 1] << 8) |
		(bytearray[index + 2] << 16) | (bytearray[index + 3] << 24);
		return rval;
	}

	public static uint ByteArrayToUInt(byte[] bytearray, int index) {
		uint rval;
		rval = (uint)(bytearray[index] | (bytearray[index + 1] << 8) |
		(bytearray[index + 2] << 16) | (bytearray[index + 3] << 24));
		return rval;
	}

	public static void IntToByteArray(uint inval, byte[] bytearray, ref int index) {
		IntToByteArray(inval, bytearray, index);
		index += 4;
	}

	public static void IntToByteArray(int inval, byte[] bytearray, ref int index) {
		IntToByteArray(inval, bytearray, index);
		index += 4;
	}

	public static int ByteArrayToInt(byte[] bytearray, ref int index) {
		int rval = ByteArrayToInt(bytearray, index);
		index += 4;
		return rval;
	}

	public static uint ByteArrayToUInt(byte[] bytearray, ref int index) {
		uint rval = ByteArrayToUInt(bytearray, index);
		index += 4;
		return rval;
	}

	///////////////////// LONG /////////////////////

	public static long ByteArrayToLong(byte[] bytearray, int index) {
		return BitConverter.ToInt64(bytearray, index);
	}

	public static ulong ByteArrayToULong(byte[] bytearray, int index) {
		return BitConverter.ToUInt64(bytearray, index);
	}

	public static void LongToByteArray(long inval, byte[] bytearray, int index) {
		byte[] bytes = BitConverter.GetBytes(inval);
		int i, len;
		len = bytes.Length;
		for (i = 0; i < len; i++) {
			bytearray[i + index] = bytes[i];
		}
	}

	public static void LongToByteArray(ulong inval, byte[] bytearray, int index) {
		byte[] bytes = BitConverter.GetBytes(inval);
		int i, len;
		len = bytes.Length;
		for (i = 0; i < len; i++) {
			bytearray[i + index] = bytes[i];
		}
	}

	public static long ByteArrayToLong(byte[] bytearray, ref int index) {
		long rval = ByteArrayToLong(bytearray, index);
		index += 8;
		return rval;
	}

	public static ulong ByteArrayToULong(byte[] bytearray, ref int index) {
		ulong rval = ByteArrayToULong(bytearray, index);
		index += 8;
		return rval;
	}

	public static void LongToByteArray(long inval, byte[] bytearray, ref int index) {
		LongToByteArray(inval, bytearray, index);
		index += 8;
	}

	public static void LongToByteArray(ulong inval, byte[] bytearray, ref int index) {
		LongToByteArray(inval, bytearray, index);
		index += 8;
	}

	///////////////////// CHAR /////////////////////

	public static void CharToByteArray(char[] pat, byte[] bytearray, int index) {
		int i;
		int len = pat.Length;

		for (i = 0; i < len; i++) {
			bytearray[index + i] = (byte)pat[i];
		}
	}

	public static char[] ByteArrayToChar(byte[] bytearray, int index, int len) {
		int i;
		char[] pat = new char[len];

		for (i = 0; i < len; i++) {
			pat[i] = (char)bytearray[index + i];
		}
		return pat;
	}

	public static void CharToByteArray(char[] pat, byte[] bytearray, ref int index) {
		int len = pat.Length;
		CharToByteArray(pat, bytearray, index);
		index += len;
	}

	public static char[] ByteArrayToChar(byte[] bytearray, ref int index, int len) {
		char[] pat = ByteArrayToChar(bytearray, index, len);
		index += len;
		return pat;
	}

	///////////////////// STRING /////////////////////

	public static int GetEncodedStringLen(string pat) {
		byte[] encoded = Encoding.Unicode.GetBytes(pat);
		return encoded.Length + 2;
	}

	public static void StringToByteArray(string pat, byte[] bytearray, ref int index) {
		int i;
		byte[] encoded = Encoding.Unicode.GetBytes(pat);
		short len = (short)encoded.Length;
		ShortToByteArray(len, bytearray, index);
		for (i = 0; i < len; i++) {
			bytearray[index + 2 + i] = (byte)encoded[i];
		}
		index += 2 + len;
	}

	public static string ByteArrayToString(byte[] bytearray, ref int index) {
		short len = ByteArrayToShort(bytearray, index);
		string rval = Encoding.Unicode.GetString(bytearray, index + 2, len);
		index += 2 + len;
		return rval;
	}

	///////////////////// FLOAT /////////////////////

	public static float ByteArrayToFloat(byte[] bytearray, int index) {
		return BitConverter.ToSingle(bytearray, index);
	}

	public static void FloatToByteArray(float inval, byte[] bytearray, int index) {
		byte[] bytes = BitConverter.GetBytes(inval);
		int i, len;
		len = bytes.Length;
		for (i = 0; i < len; i++) {
			bytearray[i + index] = bytes[i];
		}
	}

	public static float ByteArrayToFloat(byte[] bytearray, ref int index) {
		float rval = ByteArrayToFloat(bytearray, index);
		index += 4;
		return rval;
	}

	public static void FloatToByteArray(float inval, byte[] bytearray, ref int index) {
		FloatToByteArray(inval, bytearray, index);
		index += 4;
	}

	///////////////////// DOUBLE /////////////////////

	public static double ByteArrayToDouble(byte[] bytearray, int index) {
		return BitConverter.ToDouble(bytearray, index);
	}

	public static void DoubleToByteArray(double inval, byte[] bytearray, int index) {
		byte[] bytes = BitConverter.GetBytes(inval);
		int i, len;
		len = bytes.Length;
		for (i = 0; i < len; i++) {
			bytearray[i + index] = bytes[i];
		}
	}

	public static double ByteArrayToDouble(byte[] bytearray, ref int index) {
		double rval = ByteArrayToDouble(bytearray, index);
		index += 8;
		return rval;
	}

	public static void DoubleToByteArray(double inval, byte[] bytearray, ref int index) {
		DoubleToByteArray(inval, bytearray, index);
		index += 8;
	}

	///////////////////// Utilities /////////////////////

	public static bool CheckHeader(char[] pat, byte[] bytearray, int index) {
		bool res = true;
		int i, len;

		len = pat.Length;
		for (i = 0; i < len; i++) {
			if (pat[i] != bytearray[index + i]) res = false;
		}
		return res;
	}

	public static void InsertChecksum(byte[] blob) {
		uint checksum = 0;
		int i;

		if (blob.Length == 0) return;
		for (i = 0; i < blob.Length - 4; i++) {
			checksum += blob[i];
		}
		IntToByteArray(checksum, blob, blob.Length - 4);
	}

	public static bool VerifyCheckSum(byte[] blob) {
		uint rchecksum;
		uint checksum = 0;
		int i;

		if (blob.Length == 0) return false;
		for (i = 0; i < blob.Length - 4; i++) {
			checksum += blob[i];
		}
		rchecksum = ByteArrayToUInt(blob, blob.Length - 4);
		return (checksum == rchecksum);
	}
}