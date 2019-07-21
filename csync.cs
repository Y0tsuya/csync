// Revision History
// v4.0.0 - Port from vbscript to C#
//			move delete sync up in order
//			fix deletesync not traversing subdirectories
//			Remove Delimon copy because it inserts extra character after extension
//			Put Delimon back - make sure tccle uses classis console
//			trim '\\' from source/target paths
//			file copy/delete exception now stops sync process
//			Rework Deletesync not deleting stale files in target directory
//			fix exception handling in sendmail, remove ex.innerexception
// v4.5.0 - Switch to AlphaFS
//			Add Attach log option
//			change DeleteSync Directory delete logic
//			Remove [SUBDIR] log message
//			continue on error, change error to warning
//			Retouch newer dest files to source timestamp
// v5.0.0 - Move to MD5 hash and file table
//			Use skip/match name list for files
//			For move, match MD5 + file size
//			MD5.Generate -> MD5.Read
//			Fix MD5=null bug when comparing
// v5.1.0 - Add more elapsed time indicators
//			Rework console move/copy/delete reporting
// v5.2.0 - Move dupes list to dupefile
//			Scan writes to same line, not logged
// v5.3.0 - Better clearing of same-line prints
//			Add elapsed time after dupe analysis
//			Use dictionary for searches
// v5.5.0 - Multithread tree scanning
//			Polish multi-line printing
//			Add dupes count
//			Add directory count
//			Change file scanner to exit on all exceptions
//			Scan complete indicator is wonky
//			Fix email log attach
//			More precise dupe finding
// v5.6.0 - Dumpfile error handling
//			Size display for delete/update/copy
//			size display add TB scale
//			Handle file not exist exception during scanning
//			Clean up error/warning messages
// v5.7.0 - Change GUI
// v5.7.5 - GUI tuning
//			Updatecopy logic to include size difference
// v5.8.0 - GUI tuning
//			Add file and path count during scan
// v5.9.0 - Save temporary scan file to resume aborted sync
//			Can abort sync by pressing [ESC] key
//			Can resume partial transaction after abort
//			Only save temp scan file after 1.[ESC] key + 2.successful scanning
//			Use more CleanExit() on errors
//			Add copy/update/delete sum to log
//			Add eta indicator
// v6.0.0 - Enable SSL
//			All exceptions now log with [ERROR] prefix tag

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.Diagnostics;
//using Delimon.Win32.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

public class MD5Alpha {
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern uint CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr SecurityFileAttributes, uint dwCreationDisposition, uint dwFlagAndAttributes, IntPtr hTemplateFile);
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern bool DeleteFileW(string lpFileName);
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern bool CloseHandle(uint handle);
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern bool ReadFile(uint hFile, [Out] byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	static extern int WriteFile(uint hFile, [In] byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

	public const uint GENERIC_ALL = 0x10000000;
	public const uint GENERIC_EXECUTE = 0x20000000;
	public const uint GENERIC_WRITE = 0x40000000;
	public const uint GENERIC_READ = 0x80000000;
	public const uint FILE_SHARE_READ = 0x00000001;
	public const uint FILE_SHARE_WRITE = 0x00000002;
	public const uint FILE_SHARE_DELETE = 0x00000004;
	public const uint CREATE_NEW = 1;
	public const uint CREATE_ALWAYS = 2;
	public const uint OPEN_EXISTING = 3;
	public const uint OPEN_ALWAYS = 4;
	public const uint TRUNCATE_EXISTING = 5;
	public const int FILE_ATTRIBUTE_NORMAL = 0x80;

	public long minsize = 0, maxsize = long.MaxValue;
	public string hashString;

	public MD5Alpha() {
	}

	public string GetHashString(byte[] hash) {
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
	}

	public byte[] Generate(string filename) {
		byte[] hash;
		string fullpath = Alphaleonis.Win32.Filesystem.Path.GetFullPath(filename);

		try {
			using (var md5 = MD5.Create()) {
				using (var stream = Alphaleonis.Win32.Filesystem.File.OpenRead(filename)) {
					hash = md5.ComputeHash(stream);
					return hash;
				}
			}
		} catch (IOException ex) {
			string buf = ex.Message;
			return null;
		}
	}

	public byte[] Read(string filename) {
		byte[] hash = new byte[16];
		uint bytesread = 0;
		uint handle = 0;
		string fullpath = Alphaleonis.Win32.Filesystem.Path.GetFullPath(filename);

		try {
			handle = CreateFileW(filename + ":md5", GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
		} catch (Exception ex) {
			string buf = ex.Message;
			return null;
		}
		if (handle == 0xFFFFFFFF) {	// no MD5
			return null;
		}
		ReadFile(handle, hash, 16, out bytesread, IntPtr.Zero);
		CloseHandle(handle);
		return hash;
	}

	public bool Verify(string filename) {
		byte[] storedhash = new byte[16];
		byte[] genhash;
		uint bytesread = 0;
		uint handle = 0;

		string fullpath = Alphaleonis.Win32.Filesystem.Path.GetFullPath(filename);
		try {
			handle = CreateFileW(filename + ":md5", GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
		} catch (Exception ex) {
			string buf = ex.Message;
			return false;
		}
		if (handle == 0xffffffff) {
			return false;
		}
		ReadFile(handle, storedhash, 16, out bytesread, IntPtr.Zero);
		CloseHandle(handle);
		genhash = Generate(filename);
		if (genhash.SequenceEqual(storedhash)) return true;
		else return false;
	}

	public bool Attach(string filename) {
		uint byteswritten = 0;
		uint handle = 0;
		byte[] genhash;
		DateTime modify;

		string fullpath = Alphaleonis.Win32.Filesystem.Path.GetFullPath(filename);
		modify = Alphaleonis.Win32.Filesystem.File.GetLastWriteTime(filename);
		try {
			handle = CreateFileW(filename + ":md5", GENERIC_READ, FILE_SHARE_READ, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
		} catch (Exception ex) {
			string buf = ex.Message;
			return false;
		}
		if (handle != 0xFFFFFFFF) {
			return false;
		}
		genhash = Generate(filename);
		try {
			handle = CreateFileW(filename + ":md5", GENERIC_WRITE, FILE_SHARE_WRITE, IntPtr.Zero, OPEN_ALWAYS, 0, IntPtr.Zero);
		} catch (Exception ex) {
			string buf = ex.Message;
			return false;
		}
		WriteFile(handle, genhash, 16, out byteswritten, IntPtr.Zero);
		CloseHandle(handle);
		Alphaleonis.Win32.Filesystem.File.SetLastWriteTime(filename, modify);
		return true;
	}

	public bool Detach(string filename) {
		string fullpath = Alphaleonis.Win32.Filesystem.Path.GetFullPath(filename);

		try {
			DeleteFileW(filename + ":md5");
		} catch (Exception ex) {
			string buf = ex.Message;
			return false;
		}
		return true;
	}
}

public class XYConsole {

	public class XYStringParam {
		public int X;
		public int Y;
		public int Lim;
		public XYStringParam(int x, int y, int lim) {
			X = x;
			Y = y;
			Lim = lim;
		}
	}

	public struct LogStruct {
		public string Text;
		public ConsoleColor Color;
	}
	private int Offset;
	private int Lines;
	private readonly object consoleLock = new object();
	public LogStruct[] Log;
	private XYStringParam LogXY;

	public XYConsole(int linesToHandle, XYStringParam logXY, int logLines) {
		int i;

		Offset = Console.CursorTop;
		Lines = linesToHandle;
		for (i = 0 ; i < Lines ; i++) Console.WriteLine();
		Log = new LogStruct[logLines];
		for (i = 0 ; i < logLines ; i++) {
			Log[i] = new LogStruct();
			Log[i].Text = "";
			Log[i].Color = ConsoleColor.White;
		}
		LogXY = logXY;
		Console.CursorVisible = false;
	}

	public void PrintRaw(XYStringParam xy , string text, ConsoleColor color) {
		PrintRaw(xy.X, xy.Y, text, color);
	}

	public void PrintRaw(int x, int y, string text, ConsoleColor color) {
		Console.SetCursorPosition(x, y);
		System.Console.ForegroundColor = color;
		Console.WriteLine(text);
	}

	public void Finish() {
		Console.SetCursorPosition(0, Lines);
		System.Console.ResetColor();
		Console.CursorVisible = true;
	}

	public string FitText(string text, int charlimit) {
		string truncated;
		int i;
		int txtlen = text.Length;
		int len = 0;

		for (i = 0 ; i < txtlen ; i++) {
			if (text[i] >= 0x0600) len += 2;
			else len++;
			if (len > charlimit) break;
		}
		truncated = text.Substring(0, i);
		return truncated;
	}

	public void WriteAt(XYStringParam xy, string text, ConsoleColor color, int lineNum) {
		WriteAt(xy.X, xy.Y+lineNum, text, xy.Lim, color);
	}

	public void WriteAt(XYStringParam xy, string text, ConsoleColor color) {
		WriteAt(xy.X, xy.Y, text, xy.Lim, color);
	}

	public void WriteAt(int x, int y, string text, int charlimit, ConsoleColor color) {
		lock (consoleLock) {
			Console.SetCursorPosition(x, y);
			Console.Write(new string(' ', charlimit));
			System.Console.ForegroundColor = color;
			Console.SetCursorPosition(x, y);
			Console.Write(FitText(text, charlimit));
		}
	}

	public void AddLog(string text, ConsoleColor color) {
		int i;
		int lines = Log.Count();

		for (i = 0 ; i < lines - 1 ; i++) {
			Log[i] = Log[i + 1];
		}
		Log[lines - 1].Text = text;
		Log[lines - 1].Color = color;
		DisplayLog();
	}

	private void DisplayLog() {
		int i;
		int lines = Log.Count();

		for (i = 0 ; i < lines ; i++) {
			WriteAt(LogXY, Log[i].Text, Log[i].Color, i);
		}
	}
}

namespace csync {
	public class csync {

		static string version = "v6.0.0";
		static string template =
@"╔═════════╦═══════════════════════════════════════════════╦═══════╦═════════╦═══════════════╗
║ Project ║                                               ║ Paths ║  Files  ║ csync v1.0.0  ║
║ Source  ║                                               ║       ║         ╠═══════════════╣
║ Target  ║                                               ║       ║         ║               ║
╠═══════╦═╩════════╦══════╦══════════╦═════════╦══════════╬═════╦═╩════════╦╩═══════════════╣
║ Phase ║          ║ Time ║          ║ Elapsed ║          ║ ETA ║          ║                ║
╠═══════╩══╦═══════╩══════╩══════════╩═════════╩══════════╩═════╩══════════╩═════════╦══════╣
║ Progress ║                                                                         ║      ║
╠════════╦═╩═════════════════════════════════════════════════════════════════════════╩══════╣
║ Source ║                                                                                  ║
║ Target ║                                                                                  ║
╠════════╩══════════════════════════════════════════════════════════════════════════════════╣
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
║                                                                                           ║
╚═══════════════════════════════════════════════════════════════════════════════════════════╝";
		enum ProjectMode { Seek, Path, Match, Skip, Network, Log };
		enum Status { Success, Warning, Error };
		[Serializable]
		public class FileTableItem {
			public int Index;
			public string Filename;
			public long Size;
			public DateTime Timestamp;
			public byte[] MD5;
			public string MD5string;
		}
		static List<string> SourcePaths, TargetPaths;

		public class SourceTargetPath {
			public string Source;
			public string Target;
		}

		public class SourceTargetPair {
			public FileTableItem Source;
			public FileTableItem Target;
			public SourceTargetPair() {
				Source = new FileTableItem();
				Target = new FileTableItem();
			}
/*			public string Source;
			public string Target;
			public int SourceIndex;
			public int TargetIndex;
			public DateTime SourceTime;
			public DateTime TargetTime;
			public long SourceSize;
			public long TargetSize;*/
		}

		static string sourceRoot, targetRoot;
		static ILookup<string, FileTableItem> SourceByName, TargetByName;
		static ILookup<string, FileTableItem> SourceByHash, TargetByHash;
		static List<FileTableItem> SourceTable, TargetTable;
		static List<SourceTargetPath> SourceTargetList;
		static List<string> SkipPathList, MatchPathList;
		static List<string> SkipNameList, MatchNameList;
		static bool matchNameValid = false;
		static bool matchPathValid = false;
		static bool network = false;
		static int netport = 25;
		static bool ssl = false;
		static bool log = false;
		static string netuser = null;
		static string netpass = null;
		static string mailserver = null;
		static string mailfrom = null;
		static string mailto = null;
		static string logpath = null;
		//		static string buf, rstring;
		static List<string> sources;
		static List<string> targets;
		static Stopwatch totaltime, phasetime;
		static string mailbody;
		static string logbody;
		enum MsgType { MSG_INFO, MSG_ALERT, MSG_WARNING, MSG_ERROR };
		static MsgType msgLevel;
		static string projectFile = "";
		static bool attachLog = false;
		static Status runtimeStatus = Status.Success;
		static bool retouch = false;
		static MD5Alpha myMD5 = new MD5Alpha();
		static XYConsole.XYStringParam ProjectXY = new XYConsole.XYStringParam(12, 1, 45);
		static XYConsole.XYStringParam VersionXY = new XYConsole.XYStringParam(78, 1, 13);
		static XYConsole.XYStringParam SourceRootXY = new XYConsole.XYStringParam(12, 2, 45);
		static XYConsole.XYStringParam TargetRootXY = new XYConsole.XYStringParam(12, 3, 45);
		static XYConsole.XYStringParam SourcePathsXY = new XYConsole.XYStringParam(60, 2, 5);
		static XYConsole.XYStringParam TargetPathsXY = new XYConsole.XYStringParam(60, 3, 5);
		static XYConsole.XYStringParam SourceFilesXY = new XYConsole.XYStringParam(68, 2, 7);
		static XYConsole.XYStringParam TargetFilesXY = new XYConsole.XYStringParam(68, 3, 7);
		static XYConsole.XYStringParam ByteXY = new XYConsole.XYStringParam(78, 3, 13);
		static XYConsole.XYStringParam IndexXY = new XYConsole.XYStringParam(77, 5, 14);
		static XYConsole.XYStringParam PhaseXY = new XYConsole.XYStringParam(10, 5, 8);
		static XYConsole.XYStringParam PhaseTimeXY = new XYConsole.XYStringParam(28, 5, 8);
		static XYConsole.XYStringParam TotalTimeXY = new XYConsole.XYStringParam(49, 5, 8);
		static XYConsole.XYStringParam EstimateXY = new XYConsole.XYStringParam(66, 5, 8);
		static XYConsole.XYStringParam ProgressXY = new XYConsole.XYStringParam(13, 7, 71);
		static XYConsole.XYStringParam PercentXY = new XYConsole.XYStringParam(87, 7, 4);
		static XYConsole.XYStringParam SourceXY = new XYConsole.XYStringParam(11, 9, 80);
		static XYConsole.XYStringParam TargetXY = new XYConsole.XYStringParam(11, 10, 80);
		static XYConsole.XYStringParam MessageXY = new XYConsole.XYStringParam(2, 12, 89);
		static XYConsole myXYConsole;
		static bool exitRequested = false;
		static bool scanCompleted = false;
		static string scanFile;

		static void TextFgColor(System.ConsoleColor color) {
			System.Console.ForegroundColor = color;
		}

		static void TextBgColor(System.ConsoleColor color) {
			System.Console.BackgroundColor = color;
		}

		static void LogMessage(string msg) {
			LogMessage(msg, MsgType.MSG_INFO, true);
		}

		static void LogMessage(string msg, MsgType type) {
			LogMessage(msg, type, true);
		}

		static void LogMessage(string msg, MsgType type, bool display) {
			ConsoleColor color = ConsoleColor.Green;
			if (type >= msgLevel) {
				switch (type) {
					case MsgType.MSG_INFO: color = ConsoleColor.Green; break;
					case MsgType.MSG_ALERT: color = ConsoleColor.Cyan; break;
					case MsgType.MSG_WARNING: color = ConsoleColor.Yellow; break;
					case MsgType.MSG_ERROR: color = ConsoleColor.Red; break;
					default: System.Console.ResetColor(); break;
				}
				if (display) myXYConsole.AddLog(msg, color);
				mailbody += msg + "\r\n";
				logbody += msg + "\r\n";
				System.Console.ResetColor();
			}
		}

		static bool MatchList(string path, List<string> List) {
			bool match;

			match = false;
			foreach (string expr in List) {
				if (expr.Length > 0) {
					if (path.Contains(expr)) {
						match = true;
						break;
					}
				}
			}
			return match;
		}

		static string MakeLongPath(string path) {
			string longpath;
			bool isUNC;

			if (path.Substring(0, 2) == "\\\\") isUNC = true;
			else isUNC = false;
			if (isUNC) {
				path = path.Replace("\\\\", "\\");
				longpath = "\\\\?\\UNC" + path;
			} else {
				longpath = "\\\\?\\" + path;
			}
			return longpath;
		}

		static string FixRootDir(string path) {
			string root;
			bool isUNC;
			char[] trimchars = { '\\' };

			if (path.Substring(0, 2) == "\\\\") isUNC = true;
			else isUNC = false;
			root = path.Trim(trimchars);
//			root = path.Replace("\\\\", "\\");
			if (isUNC) root = "\\\\" + root;
			return root;
		}

		static string StripRoot(string root, string path) {
			string rpath = path;
			char[] trimchars = { '\\' };

			rpath = rpath.Substring(root.Length);
			rpath = rpath.Trim(trimchars);
			return rpath;
		}

		const long KB = 1024;
		const long MB = KB * 1024;
		const long GB = MB * 1024;
		const long TB = GB * 1024;

		static long DecodeByteSize(string num) {
			long bytes;

			if (num.Substring(num.Length - 2) == "TB") {
				bytes = Convert.ToInt64(num.Substring(0, num.Length - 2)) * TB;
			} else if (num.Substring(num.Length - 2) == "GB") {
				bytes = Convert.ToInt64(num.Substring(0, num.Length - 2)) * GB;
			} else if (num.Substring(num.Length - 2) == "MB") {
				bytes = Convert.ToInt64(num.Substring(0, num.Length - 2)) * MB;
			} else if (num.Substring(num.Length - 2) == "KB") {
				bytes = Convert.ToInt64(num.Substring(0, num.Length - 2)) * KB;
			} else {
				bytes = Convert.ToInt64(num);
			}
			return bytes;
		}

		static string EncodeByteSize(long num) {
			string si;
			double fp = 0;
			string unit = "B";
			int dec = 0;
			string fmt = "";

			if (num > TB) {
				fp = (double)num / (double)TB;
				unit = "TB";
			} else if (num > GB) {
				fp = (double)num / (double)GB;
				unit = "GB";
			} else if (num > MB) {
				fp = (double)num / (double)MB;
				unit = "MB";
			} else if (num > KB) {
				fp = (double)num / (double)KB;
				unit = "KB";
			} else {
				fp = num;
				unit = " Byte";
			}
			if (fp >= 100) dec = 0;
			else if (fp >= 10) dec = 1;
			else dec = 2;
			fmt = "{0:F" + dec.ToString() + "}" + unit;
			si = String.Format(fmt, fp);

			return si;
		}

		static void Scan(string root, string path, List<string> paths, List<FileTableItem> table, XYConsole.XYStringParam pathXY,
			XYConsole.XYStringParam pathCountXY, XYConsole.XYStringParam fileCountXY, bool doSkip) {
			List<string> dirPaths, filePaths;
			string dirName, fileName;
			string child;
			Alphaleonis.Win32.Filesystem.FileInfo fi = null;

			if (exitRequested) return;
			if (root != path) {
				if ((!doSkip) || ((!MatchList(path, SkipPathList)) && (matchPathValid ? MatchList(path, MatchPathList) : true))) {
					paths.Add(path);
				} else {
					return;
				}
			}
			//LogMessage("[SCAN] " + path);
			myXYConsole.WriteAt(pathXY, path, ConsoleColor.Green);
			dirPaths = null;
			filePaths = null;
			try {
				dirPaths = new List<string>(Directory.EnumerateDirectories(path));
			} catch (Exception ex) {
				LogMessage("[ERROR] Cannot list directories in " + path, MsgType.MSG_ERROR);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
			}
			try {
				filePaths = new List<string>(Directory.EnumerateFiles(path));
			} catch (Exception ex) {
				LogMessage("[ERROR] Cannot list files in " + path, MsgType.MSG_ERROR);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
			}
			foreach (string dirPath in dirPaths) {
				dirName = GetFolderName(dirPath);
				//				LogMessage("[DEBUG] " + dirName);
				if ((dirName == "$RECYCLE.BIN") | (dirName == "System Volume Information")) {
					LogMessage("[SKIP] " + dirName);
				} else if ((Alphaleonis.Win32.Filesystem.File.GetAttributes(path + "\\" + dirName) & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint) {
					LogMessage("[LINK] " + dirName);
				} else {
					if ((!doSkip)||((!MatchList(path, SkipPathList)) && (matchPathValid ? MatchList(path, MatchPathList) : true))) {
						child = path + "\\" + dirName;
						Scan(root, child, paths, table, pathXY, pathCountXY, fileCountXY,doSkip);
					}
				}
			}
			foreach (string filePath in filePaths) {
				System.IO.FileAttributes fileAttributes = System.IO.FileAttributes.Normal;

				fileName = Path.GetFileName(filePath);
				try {
					fileAttributes = Alphaleonis.Win32.Filesystem.File.GetAttributes(filePath);
				} catch (Exception ex) {
					LogMessage("[WARNING] " + ex.Message, MsgType.MSG_WARNING);
					continue;
				}
				if ((fileAttributes & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint) {
					LogMessage("[LINK] " + filePath);
				} else {
					if ((!doSkip) || ((!MatchList(filePath, SkipNameList)) && (matchNameValid ? MatchList(filePath, MatchNameList) : true))) {
						FileTableItem item = new FileTableItem();
						try {
							fi = new Alphaleonis.Win32.Filesystem.FileInfo(filePath);
						} catch (Exception ex) {
							string buf = ex.Message;
							LogMessage("[WARNING] " + ex.Message, MsgType.MSG_WARNING);
							CleanExit();
						}
						item.Index = table.Count();
						item.Filename = StripRoot(root, filePath);
						item.MD5 = myMD5.Read(filePath);
						if (item.MD5 != null) item.MD5string = myMD5.GetHashString(item.MD5);
						item.Size = fi.Length;
						item.Timestamp = Alphaleonis.Win32.Filesystem.File.GetLastWriteTime(filePath);
						table.Add(item);
					}
				}
			}
			myXYConsole.WriteAt(pathCountXY, paths.Count().ToString(), ConsoleColor.Green);
			myXYConsole.WriteAt(fileCountXY, table.Count().ToString(), ConsoleColor.Green);
		}

		static void MoveFile(string source, string target) {
			string targetPath;

			targetPath = Path.GetDirectoryName(target);
			if (!Directory.Exists(targetPath)) {
				try {
					Alphaleonis.Win32.Filesystem.Directory.CreateDirectory(targetPath);
				} catch (Exception ex) {
					LogMessage("[WARNING] Cannot create destination directory " + targetPath, MsgType.MSG_WARNING);
					LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
					runtimeStatus = Status.Warning;
					System.Threading.Thread.Sleep(100);
				}
			}
			try {
				Alphaleonis.Win32.Filesystem.File.Move(source, target, Alphaleonis.Win32.Filesystem.MoveOptions.None);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot move file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				runtimeStatus = Status.Warning;
				System.Threading.Thread.Sleep(100);
			}
		}

		static void CopyFile(string source, string target) {
			string targetPath;

			targetPath = Path.GetDirectoryName(target);
			if (!Directory.Exists(targetPath)) {
				try {
					Alphaleonis.Win32.Filesystem.Directory.CreateDirectory(targetPath);
				} catch (Exception ex) {
					LogMessage("[WARNING] Cannot create destination directory " + targetPath, MsgType.MSG_WARNING);
					LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
					runtimeStatus = Status.Warning;
					System.Threading.Thread.Sleep(100);
				}
			}
			try {
				Alphaleonis.Win32.Filesystem.File.Copy(source, target, true);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot copy file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				runtimeStatus = Status.Warning;
				System.Threading.Thread.Sleep(100);
			}
		}

		static void DeleteFile(string target) {
//			LogMessage("[DELETE] " + target);
			try {
				Alphaleonis.Win32.Filesystem.File.Delete(target);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot delete file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				runtimeStatus = Status.Warning;
			}
		}

		class ProgressPrinter {
			private int last_percent;
			XYConsole.XYStringParam GraphXY, PercentXY;
			XYConsole Con;

			public ProgressPrinter(XYConsole console, XYConsole.XYStringParam graph_xy, XYConsole.XYStringParam percent_xy) {
				last_percent = -1;
				GraphXY = graph_xy;
				PercentXY = percent_xy;
				Con = console;
			}

			public void Start() {
				Print(0);
			}

			public void Print(int percent) {
				int i, ticks;
				string buf;

				if (percent != last_percent) {
					last_percent = percent;
					ticks = percent * GraphXY.Lim / 100;
					buf = new string('*', ticks);
					Con.WriteAt(GraphXY, buf, ConsoleColor.Magenta);
					Con.WriteAt(PercentXY, percent.ToString() + '%', ConsoleColor.Magenta);
				}
			}

			public void Stop() {
				Print(100);
				Console.WriteLine("");
			}
		}

		static void ScanTree(string scanFile) {
			Thread sourceScanThread, targetScanThread;
			bool sourceAlive, targetAlive;
			string buf;
			bool sourceScanCompleted = false;
			bool targetScanCompleted = false;

			LogMessage("[PAIR] Source: " + sourceRoot, MsgType.MSG_ALERT);
			LogMessage("[PAIR] Target: " + targetRoot, MsgType.MSG_ALERT);
			myXYConsole.WriteAt(SourceRootXY, sourceRoot, ConsoleColor.Cyan);
			myXYConsole.WriteAt(TargetRootXY, targetRoot, ConsoleColor.Cyan);
			LogMessage("[PHASE] Scanning");
			myXYConsole.WriteAt(PhaseXY, "SCAN", ConsoleColor.Green);
			phasetime.Start();
			if (File.Exists(scanFile)) {
				LoadScan(scanFile);
				buf = String.Format("[INFO] {0} source files scanned in {1} directories", SourceTable.Count(), SourcePaths.Count());
				LogMessage(buf);
				buf = String.Format("[INFO] {0} target files scanned in {1} directories", TargetTable.Count(), TargetPaths.Count());
				LogMessage(buf);
				myXYConsole.WriteAt(SourcePathsXY, SourcePaths.Count().ToString(), ConsoleColor.Green);
				myXYConsole.WriteAt(TargetPathsXY, TargetPaths.Count().ToString(), ConsoleColor.Green);
				myXYConsole.WriteAt(SourceFilesXY, SourceTable.Count().ToString(), ConsoleColor.Green);
				myXYConsole.WriteAt(TargetFilesXY, TargetTable.Count().ToString(), ConsoleColor.Green);
			} else {
				sourceScanCompleted = false;
				targetScanCompleted = false;
				scanCompleted = false;
				sourceScanThread = new Thread(() => Scan(sourceRoot, sourceRoot, SourcePaths, SourceTable, SourceXY, SourcePathsXY, SourceFilesXY, true));
				targetScanThread = new Thread(() => Scan(targetRoot, targetRoot, TargetPaths, TargetTable, TargetXY, TargetPathsXY, TargetFilesXY, false));
				sourceScanThread.Start();
				targetScanThread.Start();
				sourceAlive = true;
				targetAlive = true;
				while (sourceAlive || targetAlive) {
					if (sourceAlive && !sourceScanThread.IsAlive) {
						if (exitRequested) {
							myXYConsole.WriteAt(SourceXY, "Scan Aborted", ConsoleColor.Green);
						} else {
							myXYConsole.WriteAt(SourceXY, "Scan Complete", ConsoleColor.Green);
							sourceScanCompleted = true;
						}
						sourceAlive = false;
					}
					if (targetAlive && !targetScanThread.IsAlive) {
						if (exitRequested) {
							myXYConsole.WriteAt(TargetXY, "Scan Aborted", ConsoleColor.Green);
						} else {
							myXYConsole.WriteAt(TargetXY, "Scan Complete", ConsoleColor.Green);
							targetScanCompleted = true;
						}
						targetAlive = false;
					}
					Thread.Sleep(100);
				}
				scanCompleted = sourceScanCompleted && targetScanCompleted;
				buf = String.Format("[INFO] {0} source files scanned in {1} directories", SourceTable.Count(), SourcePaths.Count());
				LogMessage(buf);
				buf = String.Format("[INFO] {0} target files scanned in {1} directories", TargetTable.Count(), TargetPaths.Count());
				LogMessage(buf);
			}
			LogMessage("[INFO] Time elapsed: " + totaltime.Elapsed);
		}

		static void FindDupes(ILookup<string, FileTableItem> hashLookup, string dumpFile) {
			int i, items;
			ProgressPrinter progress = new ProgressPrinter(myXYConsole, ProgressXY, PercentXY);
			TextWriter tw = null;
			int count = 0;

			items = hashLookup.Count();
			if (exitRequested) return;
			/*			if (!File.Exists(dumpFile)) {
							LogMessage("[ERROR] Cannot open " + dumpFile + " for writing.", MsgType.MSG_ERROR);
							CleanExit();
							return;
						}*/
			try {
				tw = new StreamWriter(dumpFile);
			} catch (Exception ex) {
				LogMessage("[ERROR] Cannot open " + dumpFile + " for writing.", MsgType.MSG_ERROR);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
			}
			i = 0;
			LogMessage("[DUPES] Analyzing...");
			progress.Start();
			foreach (var itemList in hashLookup) {
				i++;
				progress.Print(i / items);
				if (itemList.Count() > 1) {
					foreach (FileTableItem item in itemList) {
						tw.Write(item.Filename + "\t" + item.Size + "\t");
					}
					tw.WriteLine();
					count++;
				}
			}
			progress.Stop();
			tw.Close();
			LogMessage("[DUPES] " + count + " found");
			LogMessage("[INFO] Time elapsed: " + totaltime.Elapsed);
		}

		static string TargetWrap(string filename) {
			return targetRoot + "\\" + filename;
		}

		static string SourceWrap(string filename) {
			return sourceRoot + "\\" + filename;
		}

		static void MoveSync() {
			int items, i;
//			string sourceStub, targetStub;
			bool matched;
			FileTableItem targetItem = new FileTableItem();
			ProgressPrinter progress = new ProgressPrinter(myXYConsole, ProgressXY, PercentXY);
			List<SourceTargetPair> MoveList = new List<SourceTargetPair>();
			string buf;

			if (exitRequested) return;
			phasetime.Restart();
			LogMessage("[PHASE] Move sync");
			myXYConsole.WriteAt(PhaseXY, "MOVES", ConsoleColor.Green);
			LogMessage("[MOVESYNC] Analyzing...");
			progress.Start();
			foreach (FileTableItem sourceItem in SourceTable) {
				progress.Print((100 * sourceItem.Index) / SourceTable.Count());
				if (TargetByName[sourceItem.Filename].Count() == 0) matched = false;
				else matched = true;
				if (!matched) {
					// search for same MD5
					if (sourceItem.MD5 == null) continue;   // no MD5, go to next item
					if (TargetByHash[sourceItem.MD5string].Count() > 0) {
						foreach (FileTableItem item in TargetByHash[sourceItem.MD5string]) {
							if ((sourceItem.Size == item.Size)&&(sourceItem.Timestamp==item.Timestamp)) {
								SourceTargetPair pair = new SourceTargetPair();
								pair.Source = item;
								pair.Target = sourceItem;
								MoveList.Add(pair);
//								targetItem = item;    // update table entry with new location
//								targetItem.Filename = sourceItem.Filename;
//								TargetTable[item.Index] = targetItem;
							}
						}
					}
				}
			}
			progress.Stop();
			LogMessage("[MOVESYNC] " + MoveList.Count() + " files to move");
			LogMessage("[MOVESYNC] Moving...");
			progress.Start();
			items = MoveList.Count();
			myXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0 ; i < items ; i++) {
				if (exitRequested) return;
				buf = String.Format("[MOVE] ({0}/{1}) {2}", i + 1, items, TargetWrap(MoveList[i].Source.Filename));
				LogMessage(buf);
				LogMessage("   ==> " + TargetWrap(MoveList[i].Target.Filename), MsgType.MSG_INFO, false);
				myXYConsole.WriteAt(SourceXY, MoveList[i].Source.Filename, ConsoleColor.Green);
				myXYConsole.WriteAt(TargetXY, MoveList[i].Target.Filename, ConsoleColor.Green);
				MoveFile(TargetWrap(MoveList[i].Source.Filename), TargetWrap(MoveList[i].Target.Filename));
				myXYConsole.WriteAt(IndexXY, (i + 1) + "/" + items, ConsoleColor.Magenta);
				TargetTable[MoveList[i].Source.Index].Filename = MoveList[i].Target.Filename;
				progress.Print((100 * i) / items);
			}
			progress.Stop();
			LogMessage("[INFO] Time elapsed: " + totaltime.Elapsed);
		}

		static void CopySync() {
			int items, i;
			bool matched;
			FileTableItem targetItem = new FileTableItem();
			ProgressPrinter progress = new ProgressPrinter(myXYConsole, ProgressXY, PercentXY);
			List<SourceTargetPair> TouchList = new List<SourceTargetPair>();
			List<SourceTargetPair> CopyList = new List<SourceTargetPair>();
			List<SourceTargetPair> UpdateList = new List<SourceTargetPair>();
			string buf;
			long copySum, updateSum;
			long runningSum;
			TimeSpan ETA;
			TimeSpan phaseStart;
			TimeSpan elapsed;
			long totalTicks;

			if (exitRequested) return;
			phasetime.Restart();
			LogMessage("[PHASE] Copy sync");
			myXYConsole.WriteAt(PhaseXY, "COPY", ConsoleColor.Green);
			LogMessage("[COPYSYNC] Analyzing...");
			progress.Start();
//			copySum = 0; updateSum = 0;
			foreach (FileTableItem sourceItem in SourceTable) {
				progress.Print((100 * sourceItem.Index) / SourceTable.Count());
				if (TargetByName[sourceItem.Filename].Count() == 0) {
					matched = false;
				} else {
					targetItem = TargetByName[sourceItem.Filename].ElementAt(0);
					matched = true;
				}
/*				SourceTargetPair pair = new SourceTargetPair();
				pair.Source = sourceRoot + "\\" + sourceItem.Filename;
				pair.Target = targetRoot + "\\" + sourceItem.Filename;
				pair.SourceTime = sourceItem.Timestamp;
				pair.TargetTime = targetItem.Timestamp;
				pair.SourceSize = sourceItem.Size;
				pair.TargetSize = targetItem.Size;*/
				if (matched) {
					if (sourceItem.Timestamp.CompareTo(targetItem.Timestamp) < 0) {
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						TouchList.Add(pair);
						runtimeStatus = Status.Warning;
					} else if (sourceItem.Timestamp.CompareTo(targetItem.Timestamp) > 0) {
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						UpdateList.Add(pair);
//						updateSum = updateSum + sourceItem.Size;
					} else if (sourceItem.Size != targetItem.Size) {
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						UpdateList.Add(pair);
					}
				} else {
					SourceTargetPair pair = new SourceTargetPair();
					pair.Source = sourceItem;
					pair.Target = sourceItem;
					CopyList.Add(pair);
//					copySum = copySum + sourceItem.Size;
				}
			}
			updateSum = UpdateList.Sum(item => item.Source.Size);
			copySum = CopyList.Sum(item => item.Source.Size);
			progress.Stop();
			LogMessage("[COPYSYNC] " + TouchList.Count() + " files are newer");
			LogMessage("[COPYSYNC] " + UpdateList.Count() + " files to update");
			LogMessage("[COPYSYNC] " + CopyList.Count() + " files to copy");
			LogMessage("[COPYSYNC] Touching (if enabled)...");
			items = TouchList.Count();
			for (i = 0 ; i < items ; i++) {
				if (exitRequested) return;
				LogMessage("[WARNING] Destination file is newer: " + TouchList[i].Target.Filename, MsgType.MSG_WARNING);
				if (retouch) {
					try {
						Alphaleonis.Win32.Filesystem.File.SetLastWriteTime(TargetWrap(TouchList[i].Target.Filename), TouchList[i].Source.Timestamp);
						myXYConsole.WriteAt(TargetXY, TouchList[i].Target.Filename, ConsoleColor.Green);
						LogMessage("[TOUCH] Target timestamp updated");
					} catch (IOException ex) {
						buf = ex.Message;
						TextFgColor(System.ConsoleColor.Red);
						LogMessage("[WARNING] Target timestamp update unsuccessful", MsgType.MSG_WARNING);
					}
				}
			}
			myXYConsole.WriteAt(PhaseXY, "UPDATE", ConsoleColor.Green);
			runningSum = 0;
			phaseStart = phasetime.Elapsed;
			progress.Start();
			buf = String.Format("[COPYSYNC] {0} files, {1} to update", UpdateList.Count(), EncodeByteSize(updateSum));
			LogMessage(buf);
			LogMessage("[COPYSYNC] Updating...");
			items = UpdateList.Count();
			myXYConsole.WriteAt(ByteXY, "0/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
			myXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0 ; i < items ; i++) {
				if (exitRequested) return;
				buf = String.Format("[UPDATE] ({0}/{1}) {2}", i + 1, items, TargetWrap(UpdateList[i].Target.Filename));
				LogMessage(buf);
				LogMessage(UpdateList[i].Source.Timestamp.ToShortDateString() + " " + UpdateList[i].Source.Timestamp.ToShortTimeString() + " <> " +
							UpdateList[i].Target.Timestamp.ToShortDateString() + " " + UpdateList[i].Target.Timestamp.ToShortTimeString(), MsgType.MSG_INFO, false);
				myXYConsole.WriteAt(SourceXY, SourceWrap(UpdateList[i].Source.Filename), ConsoleColor.Green);
				myXYConsole.WriteAt(TargetXY, TargetWrap(UpdateList[i].Target.Filename), ConsoleColor.Green);
				CopyFile(SourceWrap(UpdateList[i].Source.Filename), TargetWrap(UpdateList[i].Target.Filename));
				runningSum += UpdateList[i].Source.Size;
				myXYConsole.WriteAt(ByteXY, EncodeByteSize(runningSum) + "/" + EncodeByteSize(updateSum+copySum), ConsoleColor.Magenta);
				myXYConsole.WriteAt(IndexXY, (i + 1) + "/" + (UpdateList.Count() + CopyList.Count()), ConsoleColor.Magenta);
				elapsed = phasetime.Elapsed - phaseStart;
				totalTicks = (long)((double)(updateSum + copySum) / (double)(runningSum) * elapsed.Ticks);
				ETA = new TimeSpan(totalTicks - elapsed.Ticks);
				myXYConsole.WriteAt(EstimateXY, ETA.ToString(), ConsoleColor.Cyan);
				TargetTable[UpdateList[i].Target.Index] = SourceTable[UpdateList[i].Source.Index];
				TargetTable[UpdateList[i].Target.Index].Index = UpdateList[i].Target.Index;
				progress.Print((int)((100 * runningSum) / updateSum));
			}
			progress.Stop();
			buf = String.Format("[COPYSYNC] {0} transferred during update phase", EncodeByteSize(updateSum));
			LogMessage(buf);
			myXYConsole.WriteAt(PhaseXY, "COPY", ConsoleColor.Green);
			runningSum = 0;
			progress.Start();
			buf = String.Format("[COPYSYNC] {0} files, {1} to copy", CopyList.Count(), EncodeByteSize(copySum));
			LogMessage(buf);
			LogMessage("[COPYSYNC] Copying...");
			items = CopyList.Count();
			myXYConsole.WriteAt(ByteXY, "0/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
			myXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0 ; i < items ; i++) {
				if (exitRequested) return;
				buf = String.Format("[COPY] ({0}/{1}) {2}", i + 1, items, SourceWrap(CopyList[i].Source.Filename));
				LogMessage(buf);
				LogMessage("   ==> " + TargetWrap(CopyList[i].Target.Filename), MsgType.MSG_INFO, false);
				myXYConsole.WriteAt(SourceXY, SourceWrap(CopyList[i].Source.Filename), ConsoleColor.Green);
				myXYConsole.WriteAt(TargetXY, TargetWrap(CopyList[i].Target.Filename), ConsoleColor.Green);
				CopyFile(SourceWrap(CopyList[i].Source.Filename), TargetWrap(CopyList[i].Target.Filename));
				runningSum += CopyList[i].Source.Size;
				myXYConsole.WriteAt(ByteXY, EncodeByteSize(updateSum + runningSum) + "/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
				myXYConsole.WriteAt(IndexXY, (i + UpdateList.Count() + 1) + "/" + (UpdateList.Count() + CopyList.Count()), ConsoleColor.Magenta);
				elapsed = phasetime.Elapsed - phaseStart;
				totalTicks = (long)((double)(updateSum + copySum)/(double)(runningSum + updateSum) * elapsed.Ticks);
				ETA = new TimeSpan(totalTicks - elapsed.Ticks);
				myXYConsole.WriteAt(EstimateXY, ETA.ToString(), ConsoleColor.Cyan);
				TargetTable.Add(SourceTable[CopyList[i].Source.Index]);
				TargetTable[TargetTable.Count() - 1].Index = TargetTable.Count() - 1;
				progress.Print((int)((100 * runningSum) / copySum));
			}
			progress.Stop();
			buf = String.Format("[COPYSYNC] {0} transferred during copy phase", EncodeByteSize(copySum));
			LogMessage(buf);
			LogMessage("[INFO] Time elapsed: " + totaltime.Elapsed);
		}

		static void DeleteSync() {
			int items, i;
			string sourceStub, targetStub;
			bool matched;
//			string sourcePath;
			ProgressPrinter progress = new ProgressPrinter(myXYConsole, ProgressXY, PercentXY);
			List<FileTableItem> DeleteList = new List<FileTableItem>();
			string buf;
			long deleteSum;

			if (exitRequested) return;
			phasetime.Restart();
			LogMessage("[PHASE] Delete sync");
			myXYConsole.WriteAt(PhaseXY, "DELETE", ConsoleColor.Green);
			LogMessage("[DELSYNC] Analyzing...");
			progress.Start();
			deleteSum = 0;
			foreach (FileTableItem targetItem in TargetTable) {
				progress.Print((100 * targetItem.Index) / TargetTable.Count());
				if (SourceByName[targetItem.Filename].Count() == 0) matched = false;
				else matched = true;
				if (!matched) {
					DeleteList.Add(targetItem);
					deleteSum = deleteSum + targetItem.Size;
				}
			}
			deleteSum= DeleteList.Sum(item => item.Size);
			progress.Stop();
			buf = String.Format("[DELSYNC] {0} files, {1} to delete", DeleteList.Count(), EncodeByteSize(deleteSum));
			LogMessage(buf);
			LogMessage("[DELSYNC] Deleting...");
			progress.Start();
			items = DeleteList.Count();
			myXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0 ; i < items ; i++) {
				if (exitRequested) return;
				progress.Print((100 * i) / items);
				buf = String.Format("[DELETE] ({0}/{1}) {2}", i + 1, items, TargetWrap(DeleteList[i].Filename));
				LogMessage(buf);
				myXYConsole.WriteAt(TargetXY, TargetWrap(DeleteList[i].Filename), ConsoleColor.Green);
				DeleteFile(TargetWrap(DeleteList[i].Filename));
				myXYConsole.WriteAt(IndexXY, (i + 1) + "/" + items, ConsoleColor.Magenta);
				TargetTable.Remove(DeleteList[i]);
			}
			progress.Stop();
			// re-index TargetTable after element deletion
			items = TargetTable.Count();
			for (i = 0 ; i < items ; i++) {
				TargetTable[i].Index = i;
			}
			LogMessage("[DELSYNC] Removing trees...");
			progress.Start();
			items = TargetPaths.Count();
//			foreach (string targetPath in TargetPaths) {
			for (i=0 ;i<items ;i++) {
				if (exitRequested) return;
				progress.Print((100 * i) / items);
				targetStub = StripRoot(targetRoot, TargetPaths[i]);
				matched = false;
//				items = SourcePaths.Count();
				foreach (string sourcePath in SourcePaths) {
//					for (j = 0 ; j < items ; j++) {
//					sourcePath = SourcePaths[j];
					sourceStub = StripRoot(sourceRoot, sourcePath);
					if (sourceStub == targetStub) {
						matched = true;
						break;
					}
				}
				if (!matched) {
					LogMessage("[DELTREE] " + TargetPaths[i]);
					try {
						myXYConsole.WriteAt(TargetXY, TargetPaths[i], ConsoleColor.Green);
						Directory.Delete(TargetPaths[i], true);
					} catch (Exception ex) {
						LogMessage("[WARNING] Cannot delete directory " + TargetPaths[i], MsgType.MSG_WARNING);
						LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
						runtimeStatus = Status.Warning;
					}
				}
			}
			progress.Stop();
			buf = String.Format("[DELSYNC] {0} total deleted", EncodeByteSize(deleteSum));
			LogMessage("[INFO] Time elapsed: " + totaltime.Elapsed);
		}

		static string GetFolderName(string path) {
			char[] delims = new char[] { '\\' };
			string[] tokens;
			tokens = path.Split(delims, StringSplitOptions.RemoveEmptyEntries);
			return tokens[tokens.Count() - 1];
		}

		static void LoadProject(string prjpath) {
			ProjectMode mode = ProjectMode.Seek;
			SkipNameList = new List<string>();
			SkipPathList = new List<string>();
			MatchNameList = new List<string>();
			MatchPathList = new List<string>();
			sources = new List<string>();
			targets = new List<string>();
			string buf;
			char[] delims = new char[] { '=' };
			string[] tokens;

			if (!File.Exists(prjpath)) {
				LogMessage("[ERROR] Project file " + prjpath + " not found.", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			}
			StreamReader tr = new StreamReader(prjpath);
			while (!tr.EndOfStream) {
				buf = tr.ReadLine();
				buf = buf.Trim();
				if (buf == "") continue;
				if (buf[0] == '#') {
					continue;
				} else if (buf == "[Path]") {
					mode = ProjectMode.Path;
				} else if (buf == "[Match]") {
					mode = ProjectMode.Match;
				} else if (buf == "[Skip]") {
					mode = ProjectMode.Skip;
				} else if (buf == "[Network]") {
					mode = ProjectMode.Network;
					network = true;
				} else if (buf == "[Log]") {
					mode = ProjectMode.Log;
					log = true;
				} else if (mode == ProjectMode.Path) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "source":
								sources.Add(FixRootDir(tokens[1]));
								LogMessage("[SOURCE] Root #" + sources.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "target":
								targets.Add(FixRootDir(tokens[1]));
								LogMessage("[TARGET] Root #" + targets.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				} else if (mode == ProjectMode.Match) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "path":
								MatchPathList.Add(tokens[1]);
								LogMessage("[MATCH] Path #" + MatchPathList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "name":
								MatchNameList.Add(tokens[1]);
								LogMessage("[MATCH] Name #" + MatchNameList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				} else if (mode == ProjectMode.Skip) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "path":
								SkipPathList.Add(tokens[1]);
								LogMessage("[SKIP] Path #" + SkipPathList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "name":
								SkipNameList.Add(tokens[1]);
								LogMessage("[SKIP] Name #" + SkipNameList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				} else if (mode == ProjectMode.Network) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "to":
								mailto = tokens[1];
								LogMessage("[NETWORK] mailto: " + mailto, MsgType.MSG_ALERT);
								break;
							case "from":
								mailfrom = tokens[1];
								LogMessage("[NETWORK] mailfrom: " + mailfrom, MsgType.MSG_ALERT);
								break;
							case "server":
								mailserver = tokens[1];
								LogMessage("[NETWORK] mailserver: " + mailserver, MsgType.MSG_ALERT);
								break;
							case "port":
								netport = Convert.ToInt32(tokens[1]);
								LogMessage("[NETWORK] port: " + netport, MsgType.MSG_ALERT);
								break;
							case "user":
								netuser = tokens[1];
								LogMessage("[NETWORK] userid: " + netuser, MsgType.MSG_ALERT);
								break;
							case "pass":
								netpass = tokens[1];
								LogMessage("[NETWORK] passwd: " + netpass, MsgType.MSG_ALERT);
								break;
							case "ssl":
								if ((tokens[1]=="true")||(tokens[1]=="yes")) {
									ssl = true;
								} else {
									ssl = false;
								}
								LogMessage("[NETWORK] SSL: " + (ssl?"Yes":"No"), MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				} else if (mode == ProjectMode.Log) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "path":
								logpath = tokens[1];
								LogMessage("[LOG] path: " + logpath, MsgType.MSG_ALERT);
								break;
							case "attach":
								if (tokens[1].ToLower() == "yes") {
									attachLog = true;
								}
								LogMessage("[LOG] attach: " + attachLog, MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				}
			}
			tr.Close();
			if (MatchNameList.Count() > 0) matchNameValid = true;
			if (MatchPathList.Count() > 0) matchPathValid = true;
		}

		static void SendMail(string host, int port, bool ssl, string user, string pass, string from, string to, string subject, string body) {
			SmtpClient client = new SmtpClient(host, port);
			MailAddress addrFrom = new MailAddress(from, "cSync", System.Text.Encoding.UTF8);
			MailAddress addrTo = new MailAddress(to, to, System.Text.Encoding.UTF8);
			MailMessage message = new MailMessage(addrFrom, addrTo);
			client.EnableSsl = ssl;
			if ((user != "") && (pass != "")) {
				client.Credentials = new NetworkCredential(user, pass);
			}
			client.ServicePoint.MaxIdleTime = 2;
			message.Subject = subject;
			message.SubjectEncoding = System.Text.Encoding.UTF8;
			message.Body = body;
			message.BodyEncoding = System.Text.Encoding.UTF8;
			try {
				client.Send(message);
			}
			catch (ArgumentNullException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			}
			catch (ObjectDisposedException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			}
			catch (SmtpFailedRecipientsException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			}
			catch (SmtpException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
//				LogMessage(ex.InnerException.Message, MsgType.MSG_ERROR);
			}
			catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			}
			client.Dispose();
			message.Dispose();
		}

		static void SaveLog(string filename, string body) {

			try {
				StreamWriter tw = new StreamWriter(filename);
				tw.Write(body);
				tw.Flush();
				tw.Close();
				tw.Dispose();
			}

			catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
			}
		}

		static void CleanExit() {
			runtimeStatus = Status.Error;
			Notify(runtimeStatus);
			System.Console.ResetColor();
			//			Console.OutputEncoding = System.Text.Encoding.Default;
			if (scanCompleted) {
				SaveScan(scanFile);
			}
			Environment.Exit(1);
		}

		static void CheckProject() {
			int i;
			SourceTargetList = new List<SourceTargetPath>();

			if (sources.Count==0) {
				LogMessage("[ERROR] No Source root specified", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			}
			if (targets.Count==0) {
				LogMessage("[ERROR] No Target root specified", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			}
			if (sources.Count != targets.Count) {
				LogMessage("[ERROR] Source and Target root # mismatch", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			} else {
				for (i = 0; i < sources.Count; i++) {
					SourceTargetPath pair = new SourceTargetPath();
					pair.Source = sources[i];
					pair.Target = targets[i];
					SourceTargetList.Add(pair);
				}
			}
			foreach (string path in sources) {
				if (!Directory.Exists(path)) {
					LogMessage("[ERROR] Source dir not found: " + path, MsgType.MSG_ERROR);
					LogMessage("Exiting...", MsgType.MSG_ERROR);
					CleanExit();
				}
			}
			foreach (string path in targets) {
				if (!Directory.Exists(path)) {
					LogMessage("[ERROR] Target dir not found: " + path, MsgType.MSG_ERROR);
					LogMessage("Exiting...", MsgType.MSG_ERROR);
					CleanExit();
				}
			}
		}

		static void Notify(Status statusCode) {
			string logfile;
			string datepat = @"yyyy-MM-dd HH-mm-ss tt";
			string status;

			switch (statusCode) {
				case Status.Success: status = " [Success]"; break;
				case Status.Warning: status = " [Warning]"; break;
				case Status.Error: status = " [Error]"; break;
				default: status = " [Success]"; break;
			}
			LogMessage("[INFO] Logfile size: " + logbody.Length + " bytes");
			if (network) {
				LogMessage("[INFO] Sending notification email");
				if (attachLog) {
					SendMail(mailserver, netport, ssl, netuser, netpass, mailfrom, mailto, "cSync " + projectFile + status, mailbody);
				} else {
					SendMail(mailserver, netport, ssl, netuser, netpass, mailfrom, mailto, "cSync " + projectFile + status, "See Log File for details");
				}
			}
			if (log) {
				logfile = logpath + "\\csync " + Path.GetFileNameWithoutExtension(projectFile) + " " + DateTime.Now.ToString(datepat) + status + ".log";
				LogMessage("[INFO] Saving log file " + logfile);
				SaveLog(logfile, logbody);
			}
			System.Console.ResetColor();
		}

		static void OnProcessExit(object sender, EventArgs e) {

		}

		static void PrintHelp() {
			myXYConsole.AddLog("cSync " + version + " - (C)2002-2018 Bo-Yi Lin", ConsoleColor.Red);
			myXYConsole.AddLog("syntax: cSync -p [prjpath] -l verbosity -t", ConsoleColor.Red);
			myXYConsole.AddLog("verbosity: INFO, WARNING, ERROR", ConsoleColor.Red);
			myXYConsole.AddLog("-t: touch newer dest file to source timestamp", ConsoleColor.Red);
		}

		static void PrintTime() {
			while ((phasetime != null) && (totaltime != null)) {
				myXYConsole.WriteAt(PhaseTimeXY, phasetime.Elapsed.ToString(), ConsoleColor.Cyan);
				myXYConsole.WriteAt(TotalTimeXY, totaltime.Elapsed.ToString(), ConsoleColor.Cyan);
				if (Console.KeyAvailable) {
					if (Console.ReadKey(false).Key == ConsoleKey.Escape) {
						LogMessage("[WARNING] Exit requested", MsgType.MSG_WARNING);
						runtimeStatus = Status.Warning;
						exitRequested = true;
					}
				}
				Thread.Sleep(250);
			}
		}

		static void SaveScan(string filename) {
			BinaryFormatter bf = new BinaryFormatter();
			FileStream fs;

			LogMessage("[INFO] Save scan data to " + filename);
			try {
				fs = new FileStream(filename, FileMode.Create);
			} catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
				return;
			}
			bf.Serialize(fs, SourcePaths);
			bf.Serialize(fs, TargetPaths);
			bf.Serialize(fs, SourceTable);
			bf.Serialize(fs, TargetTable);
			fs.Close();
		}

		static void LoadScan(string filename) {
			BinaryFormatter bf = new BinaryFormatter();
			FileStream fs;

			LogMessage("[INFO] Load Scan data from " + filename);
			try {
				fs = new FileStream(filename, FileMode.Open);
			} catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
				return;
			}
			SourcePaths = (List<string>)bf.Deserialize(fs);
			TargetPaths = (List<string>)bf.Deserialize(fs);
			SourceTable = (List<FileTableItem>)bf.Deserialize(fs);
			TargetTable = (List<FileTableItem>)bf.Deserialize(fs);
			fs.Close();
		}

		static void Main(string[] args) {
			int i, argn;
			string dupefile = null;
			Thread timeThread;

			myXYConsole = new XYConsole(25, MessageXY, 12);
			Console.Clear();
			myXYConsole.PrintRaw(0, 0, template, ConsoleColor.Yellow);
			myXYConsole.WriteAt(VersionXY, "csync " + version, ConsoleColor.Cyan);
			SourcePaths = new List<string>();
			TargetPaths = new List<string>();
			SourceTable = new List<FileTableItem>();
			TargetTable = new List<FileTableItem>();
			totaltime = new Stopwatch();
			phasetime = new Stopwatch();
			totaltime.Start();
			timeThread = new Thread(PrintTime);
			timeThread.Start();
			if (args.Length == 0) {
				myXYConsole.Finish(); phasetime = null; totaltime = null;
				LogMessage("[ERROR] No arguments specified", MsgType.MSG_ERROR, true);
				PrintHelp();
				CleanExit();
			}
			msgLevel = MsgType.MSG_INFO;
			AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			for (argn = 0; argn < args.Length; argn++) {
				if (args[argn] == "-p") projectFile = args[argn + 1];
				if (args[argn] == "-l") {
					switch (args[argn + 1].ToLower()) {
						case "info": msgLevel = MsgType.MSG_INFO; break;
						case "alert": msgLevel = MsgType.MSG_ALERT; break;
						case "warning": msgLevel = MsgType.MSG_WARNING; break;
						case "error": msgLevel = MsgType.MSG_ERROR; break;
						default: break;
					}
				}
				if (args[argn] == "-t") {
					retouch = true;
				}
			}
			if (projectFile == "") {
				myXYConsole.Finish(); phasetime = null; totaltime = null;
				LogMessage("[ERROR] No project file specified", MsgType.MSG_ERROR, true);
				PrintHelp();
				CleanExit();
			}
			LoadProject(projectFile);
			CheckProject();
			myXYConsole.WriteAt(ProjectXY, projectFile, ConsoleColor.Cyan);
			for (i = 0; i < SourceTargetList.Count; i++) {
				SourcePaths.Clear();
				TargetPaths.Clear();
				SourceTable.Clear();
				TargetTable.Clear();
				sourceRoot = SourceTargetList[i].Source;
				targetRoot = SourceTargetList[i].Target;
				scanFile = Path.GetFileNameWithoutExtension(projectFile) + ".scan" + i.ToString();
				ScanTree(scanFile);
				SourceByName = SourceTable.ToLookup(t => t.Filename);
				SourceByHash = SourceTable.ToLookup(t => t.MD5string);
				TargetByName = TargetTable.ToLookup(t => t.Filename);
				TargetByHash = TargetTable.ToLookup(t => t.MD5string);
				if (log) dupefile = logpath + "\\csync " + Path.GetFileNameWithoutExtension(projectFile) + " dupes.txt";
				else dupefile = sourceRoot + "\\dupes.txt";
				FindDupes(SourceByHash, dupefile);
				MoveSync();
				TargetByName = TargetTable.ToLookup(t => t.Filename);	// have to re-do lookup after movesync
				TargetByHash = TargetTable.ToLookup(t => t.MD5string);
				DeleteSync();
				TargetByName = TargetTable.ToLookup(t => t.Filename);   // have to re-do lookup after movesync
				TargetByHash = TargetTable.ToLookup(t => t.MD5string);
				CopySync();
				try {
					if (exitRequested && scanCompleted) {
						SaveScan(scanFile);
					} else {
						LogMessage("[INFO] Delete scan file " + scanFile);
						File.Delete(scanFile);
					}
				} catch (Exception ex) {
					LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
					CleanExit();
				}
			}
			myXYConsole.WriteAt(PhaseXY, "COMPLETE", ConsoleColor.Green);
			Notify(runtimeStatus);
			timeThread.Abort();
			totaltime.Stop();
			phasetime.Stop();
			totaltime = null;
			phasetime = null;
			myXYConsole.Finish();
			System.Console.ResetColor();
//			Console.OutputEncoding = System.Text.Encoding.Default;
		}
	}
}
