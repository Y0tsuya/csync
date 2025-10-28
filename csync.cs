// Revision History
// v4.0.0	Port from vbscript to C#
//			move delete sync up in order
//			fix deletesync not traversing subdirectories
//			Remove Delimon copy because it inserts extra character after extension
//			Put Delimon back	make sure tccle uses classis console
//			trim '\\' from source/target paths
//			file copy/delete exception now stops sync process
//			Rework Deletesync not deleting stale files in target directory
//			fix exception handling in sendmail, remove ex.innerexception
// v4.5.0	Switch to AlphaFS
//			Add Attach log option
//			change DeleteSync Directory delete logic
//			Remove [SUBDIR] log message
//			continue on error, change error to warning
//			Retouch newer dest files to source timestamp
// v5.0.0	Move to MD5 hash and file table
//			Use skip/match name list for files
//			For move, match MD5 + file size
//			MD5.Generate -> MD5.Read
//			Fix MD5=null bug when comparing
// v5.1.0	Add more elapsed time indicators
//			Rework console move/copy/delete reporting
// v5.2.0	Move dupes list to dupefile
//			Scan writes to same line, not logged
// v5.3.0	Better clearing of same-line prints
//			Add elapsed time after dupe analysis
//			Use dictionary for searches
// v5.5.0	Multithread tree scanning
//			Polish multi-line printing
//			Add dupes count
//			Add directory count
//			Change file scanner to exit on all exceptions
//			Scan complete indicator is wonky
//			Fix email log attach
//			More precise dupe finding
// v5.6.0	Dumpfile error handling
//			Size display for delete/update/copy
//			size display add TB scale
//			Handle file not exist exception during scanning
//			Clean up error/warning messages
// v5.7.0	Change GUI
// v5.7.5	GUI tuning
//			Updatecopy logic to include size difference
// v5.8.0	GUI tuning
//			Add file and path count during scan
// v5.9.0	Save temporary scan file to resume aborted sync
//			Can abort sync by pressing [ESC] key
//			Can resume partial transaction after abort
//			Only save temp scan file after 1.[ESC] key + 2.successful scanning
//			Use more CleanExit() on errors
//			Add copy/update/delete sum to log
//			Add eta indicator
// v6.0.0	Enable SSL
//			All exceptions now log with [ERROR] prefix tag
// v6.1.0	Fix non-AlphaFS calls
// v6.1.1	Skip NULL MD5 when finding dupes
// v6.1.2	Fix SaveScan/CleanExit exception loop
// v6.1.3	Fix divide-by-zero print error
// v6.1.4	Close stream handle when it exists on writes
// v6.1.5	Remove trees in reverse order
// v6.1.6	No move if target to be moved also exists in source, case where multiple copies exist
// v6.1.7	split [SKIP] into [IGNORE] and [EXCLUDE], [SKIP] == [EXCLUDE]
// v6.1.8	Less busy Phase/Elapsed time update
// v6.1.9	Update target on MD5 mismatch
// v6.2.0	Update target on missing MD5
// v6.2.1	Update list include reason
// v6.2.2	Change mail/log string concat to StringBuilder
//			Deltree speed optimization
// v6.2.3	Handle stringbulder out of memory exception
// v6.2.4	Optional periodic auto-save log (minutes)
// v6.2.5	Rework SaveScan to support pre-scan and persistence across sessions
//			Only DoLog and DoNetwork if relevant variables are set
// v6.2.6	Add success bool to MoveFile/DeleteFile/CopyFile
//			Update scan table only on file operation success
//			File/Folder manipulation will check for ReadOnly attribute and remove it
//			Add -is -it options to ignore existence of sources and targets
// v6.2.7	Add indication for skipping or loading from scanfile 
// v7.0.0	Migrate to .NET 6
//			Remove AlphaFS
//			Update target table changes to scanfile at completion
//			BinaryFormatter works differently, scanfiles need to be reconstructed
// v7.0.1	Remove TargetPath entry if not exist
//			Remove TargetTable entry if not exist
// v7.0.2	Fix timestamp update [TOUCH] operation not saving to TargetList
// v7.1.0	Integrate HashInterop
// v7.1.1	HashInterop default to no log error
// v7.1.2	Add ExitRequested handling in Source/Target Loop
// v7.2.0	Remove BinaryFormatter for scan files, replace with custom Serializer/Deserializer
//			Fix UNC file handling in HashInterop
// v7.2.1	Add start/stop timestamps
//			If hash and size are same, then it's the same file even if timestamp is different
// v7.2.2	Edit retouch logic.  If dest file is newer, touch the file, else
//				if dest file is older, touch depends on the -retouch directive
//				if -retouch = true, dest file will be retouched
//				else dest file will be overwritten
// v7.3.0	Add simulation mode
//			Add restore mode
// v7.3.1	Change order of of reported update reason to favor file size and MD5


using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading;
using System.Reflection;
using System.Reflection.Metadata;
using CustomLibs;
//using System.IO.Filesystem.Ntfs;

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
		for (i = 0; i < Lines; i++) Console.WriteLine();
		Log = new LogStruct[logLines];
		for (i = 0; i < logLines; i++) {
			Log[i] = new LogStruct();
			Log[i].Text = "";
			Log[i].Color = ConsoleColor.White;
		}
		LogXY = logXY;
		Console.CursorVisible = false;
	}

	public void PrintRaw(XYStringParam xy, string text, ConsoleColor color) {
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

		for (i = 0; i < txtlen; i++) {
			if (text[i] >= 0x0600) len += 2;
			else len++;
			if (len > charlimit) break;
		}
		truncated = text.Substring(0, i);
		return truncated;
	}

	public void WriteAt(XYStringParam xy, string text, ConsoleColor color, int lineNum) {
		WriteAt(xy.X, xy.Y + lineNum, text, xy.Lim, color);
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

		for (i = 0; i < lines - 1; i++) {
			Log[i] = Log[i + 1];
		}
		Log[lines - 1].Text = text;
		Log[lines - 1].Color = color;
		DisplayLog();
	}

	private void DisplayLog() {
		int i;
		int lines = Log.Count();

		for (i = 0; i < lines; i++) {
			WriteAt(LogXY, Log[i].Text, Log[i].Color, i);
		}
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
		int ticks;
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

namespace csync {
	public class csync {

		static string Version = ParseVersion();//"v6.1.5";
		static string Template =
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
		enum ProjectMode { Seek, Path, Match, Ignore, Exclude, Network, Log };
		enum Status { Success, Warning, Error };

		public enum Reason { None, Timestamp, Size, MD5 };
		public class FileTableItem {
			// [serialize] 20 bytes here
			public int Index;
			public long Size;
			public DateTime Timestamp;
			// [serialize] byte MD5 exists
			public byte[] MD5;
			public string MD5string;
			public string Filename;

			public byte[] Serialize() {
				int ptr = 0;
				int fileLen = Header.GetEncodedStringLen(Filename);
				int md5Len = (MD5 != null) ? Header.GetEncodedStringLen(MD5string) : 0;
				int blocklen = 25 + ((MD5 != null) ? 16 : 0) + fileLen + md5Len;
				byte[] blob = new byte[blocklen];
				Header.IntToByteArray(Index, blob, ref ptr);
				Header.LongToByteArray(Size, blob, ref ptr);
				Header.LongToByteArray(Timestamp.Ticks, blob, ref ptr);
				blob[ptr] = (byte)((MD5 != null) ? 1 : 0); ptr++;
				Header.StringToByteArray(Filename, blob, ref ptr);
				if (MD5 != null) {
					Header.Paste(MD5, blob, ref ptr);
					Header.StringToByteArray(MD5string, blob, ref ptr);
				}
				return blob;
			}

			public void Deserialize(byte[] blob) {
				int ptr = 0;
				Index = Header.ByteArrayToInt(blob, ref ptr);
				Size = Header.ByteArrayToLong(blob, ref ptr);
				Timestamp = new DateTime(Header.ByteArrayToLong(blob, ref ptr));
				bool hasMD5 = blob[ptr] == 1; ptr++;
				Filename = Header.ByteArrayToString(blob, ref ptr);
				if (hasMD5) {
					MD5 = Header.Copy(blob, ref ptr, 16);
					MD5string = Header.ByteArrayToString(blob, ref ptr);
				}
			}

		}
		static List<string> SourcePaths, TargetPaths;

		public class SourceTargetPath {
			public string Source;
			public string Target;
		}

		public class SourceTargetPair {
			public FileTableItem Source;
			public FileTableItem Target;
			public Reason UpdateReason;
			public SourceTargetPair() {
				Source = new FileTableItem();
				Target = new FileTableItem();
				UpdateReason = Reason.None;
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

		static string SourceRoot, TargetRoot;
		static ILookup<string, FileTableItem> SourceByName, TargetByName;
		static ILookup<string, FileTableItem> SourceByHash, TargetByHash;
		static List<FileTableItem> SourceTable, TargetTable;
		static List<SourceTargetPath> SourceTargetList;
		static List<string> ExcludePathList, IgnorePathList, MatchPathList;
		static List<string> ExcludeNameList, IgnoreNameList, MatchNameList;
		static bool MatchNameValid = false;
		static bool MatchPathValid = false;
		static bool DoNetwork = false;
		static int NetPort = 25;
		static bool DoSsl = false;
		static bool DoLog = false;
		static string NetUser = null;
		static string NetPass = null;
		static string MailServer = null;
		static string MailFrom = null;
		static string MailTo = null;
		static string LogPath = null;
		//		static string buf, rstring;
		static List<string> Sources;
		static List<string> Targets;
		static Stopwatch TotalTime, PhaseTime;
		//static string mailbody;
		//static string logbody;
		static StringBuilder MailBuilder = new StringBuilder();
		static StringBuilder LogBuilder = new StringBuilder();
		enum MsgType { MSG_INFO, MSG_ALERT, MSG_WARNING, MSG_ERROR };
		static MsgType MsgLevel;
		static string ProjectFile = "";
		static bool AttachLog = false;
		static int AutoSaveInterval = 0;
		static Status RuntimeStatus = Status.Success;
		static bool Retouch = false;
		static bool ScanOnly = false;
		static HashInterop MyMD5 = new HashInterop();
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
		static XYConsole MyXYConsole;
		static bool ExitRequested = false;
		static bool ScanCompleted = false;
		static Thread TimeThread;
		static Thread AutoSaveThread;
		static string AutosaveLogFile;
		static bool ForceSourceScan = false;
		static bool ForceTargetScan = false;
		static bool IgnoreSourceExist = false;
		static bool IgnoreTargetExist = false;
		static bool AppExitCondition = false;
		static bool Simulate = false;
		static bool Restore = false;

		public const int LOG_ADD = 0;
		public const int LOG_SUB = 1;
		public const int LOG_UPD = 2;
		public const int LOG_INFO = 0;
		public const int LOG_ALERT = 1;
		public const int LOG_WARNING = 2;
		public const int LOG_ERROR = 3;
		static int LogCallBackHandler(int op, string msg, int errlvl, int subidx) {
			MsgType lvl = MsgType.MSG_INFO;
			switch (errlvl) {
				case LOG_INFO: lvl = MsgType.MSG_INFO; break;
				case LOG_ALERT: lvl = MsgType.MSG_ALERT; break;
				case LOG_WARNING: lvl = MsgType.MSG_WARNING; break;
				case LOG_ERROR: lvl = MsgType.MSG_ERROR; break;
				default: lvl = MsgType.MSG_INFO; break;
			}
			LogMessage(msg, lvl);
			return 0;
		}

		static void ProgressCallBackHandler(int max, int value) {
		}

		static void DoEventCallbackHandler() {
			//Write(".");
		}

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
			if (type >= MsgLevel) {
				switch (type) {
					case MsgType.MSG_INFO: color = ConsoleColor.Green; break;
					case MsgType.MSG_ALERT: color = ConsoleColor.Cyan; break;
					case MsgType.MSG_WARNING: color = ConsoleColor.Yellow; break;
					case MsgType.MSG_ERROR: color = ConsoleColor.Red; break;
					default: System.Console.ResetColor(); break;
				}
				if (display) MyXYConsole.AddLog(msg, color);
				MailBuilder.Append(msg + "\r\n");
				LogBuilder.Append(msg + "\r\n");
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

		static string ParseVersion() {
			Assembly execAssembly = Assembly.GetCallingAssembly();
			AssemblyName name = execAssembly.GetName();
			string ver = String.Format("{0}.{1}.{2}", name.Version.Major.ToString(), name.Version.Minor.ToString(), name.Version.Build.ToString());
			return ver;
		}

		static void Scan(string root, string path, List<string> paths, List<FileTableItem> table, XYConsole.XYStringParam pathXY,
			XYConsole.XYStringParam pathCountXY, XYConsole.XYStringParam fileCountXY, bool doExclude) {
			List<string> dirPaths, filePaths;
			string dirName, fileName;
			string child;
			FileInfo fi = null;

			if (ExitRequested) return;
			if (root != path) {
				if ((!doExclude) || ((!MatchList(path, ExcludePathList)) && (MatchPathValid ? MatchList(path, MatchPathList) : true))) {
					paths.Add(path);
				} else {
					return;
				}
			}
			//LogMessage("[SCAN] " + path);
			MyXYConsole.WriteAt(pathXY, path, ConsoleColor.Green);
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
					LogMessage("[IGNORE] " + dirName);
				} else if ((File.GetAttributes(path + "\\" + dirName) & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint) {
					LogMessage("[LINK] " + dirName);
				} else if (MatchList(path, IgnorePathList)) {
					LogMessage("[IGNORE] " + dirName);
				} else {
					if ((!doExclude) || ((!MatchList(path, ExcludePathList)) && (MatchPathValid ? MatchList(path, MatchPathList) : true))) {
						child = path + "\\" + dirName;
						Scan(root, child, paths, table, pathXY, pathCountXY, fileCountXY, doExclude);
					}
				}
			}
			foreach (string filePath in filePaths) {
				System.IO.FileAttributes fileAttributes = System.IO.FileAttributes.Normal;

				fileName = Path.GetFileName(filePath);
				try {
					fileAttributes = File.GetAttributes(filePath);
				} catch (Exception ex) {
					LogMessage("[WARNING] " + ex.Message, MsgType.MSG_WARNING);
					continue;
				}
				if ((fileAttributes & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint) {
					LogMessage("[LINK] " + filePath);
				} else if (MatchList(filePath, IgnoreNameList)) {
					LogMessage("[IGNORE] " + filePath);
				} else {
					if ((!doExclude) || ((!MatchList(filePath, ExcludeNameList)) && (MatchNameValid ? MatchList(filePath, MatchNameList) : true))) {
						FileTableItem item = new FileTableItem();
						try {
							fi = new FileInfo(filePath);
						} catch (Exception ex) {
							string buf = ex.Message;
							LogMessage("[WARNING] " + ex.Message, MsgType.MSG_WARNING);
							CleanExit();
						}
						item.Index = table.Count();
						item.Filename = StripRoot(root, filePath);
						item.MD5 = MyMD5.Read(filePath);
						if (item.MD5 != null) item.MD5string = MyMD5.GetHashString(item.MD5);
						item.Size = fi.Length;
						item.Timestamp = File.GetLastWriteTime(filePath);
						table.Add(item);
						// debug
						/*if (filePath.Contains("Master Of Gokuraku")) {
							if (item.MD5 == null) {
								LogMessage("[DEBUG] " + filePath);
								LogMessage("[DEBUG] Master of Gokuraku No MD5");
							} else {
								LogMessage("[DEBUG] Master of Gokuraku MD5 " + item.MD5string);
							}
						}*/
						//debug
					}
				}
			}
			MyXYConsole.WriteAt(pathCountXY, paths.Count().ToString(), ConsoleColor.Green);
			MyXYConsole.WriteAt(fileCountXY, table.Count().ToString(), ConsoleColor.Green);
		}

		static void RemoveReadOnlyAttribute(string target) {
			System.IO.FileAttributes fa;
			if (File.Exists(target)) {
				fa = File.GetAttributes(target);
				if ((fa & System.IO.FileAttributes.ReadOnly) == FileAttributes.ReadOnly) {
					LogMessage("[WARNING] Remove ReadOnly attribute " + target, MsgType.MSG_WARNING);
					fa = fa ^ FileAttributes.ReadOnly;
					File.SetAttributes(target, fa);
				}
			}
		}

		static bool MoveFile(string source, string target) {
			string targetPath;

			if (Simulate||Restore) return true;

			targetPath = Path.GetDirectoryName(target);
			if (!Directory.Exists(targetPath)) {
				try {
					Directory.CreateDirectory(targetPath);
				} catch (Exception ex) {
					LogMessage("[WARNING] Cannot create destination directory " + targetPath, MsgType.MSG_WARNING);
					LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
					RuntimeStatus = Status.Warning;
					System.Threading.Thread.Sleep(100);
				}
			}
			try {
				// check for read-only status and remove it
				RemoveReadOnlyAttribute(source);
				RemoveReadOnlyAttribute(target);
				File.Move(source, target, false);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot move file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				RuntimeStatus = Status.Warning;
				System.Threading.Thread.Sleep(100);
				return false;
			}
			return true;
		}

		static bool CopyFile(string source, string target) {
			string targetPath;

			if (Simulate) return true;
			targetPath = Path.GetDirectoryName(target);
			if (!Directory.Exists(targetPath)) {
				try {
					Directory.CreateDirectory(targetPath);
				} catch (Exception ex) {
					LogMessage("[WARNING] Cannot create destination directory " + targetPath, MsgType.MSG_WARNING);
					LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
					RuntimeStatus = Status.Warning;
					System.Threading.Thread.Sleep(100);
				}
			}
			try {
				RemoveReadOnlyAttribute(target);
				File.Copy(source, target, true);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot copy file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				RuntimeStatus = Status.Warning;
				System.Threading.Thread.Sleep(100);
				return false;
			}
			return true;
		}

		static bool DeleteFile(string target) {
			if (Simulate||Restore) return true;

			if (!File.Exists(target)) return true;
			try {
				RemoveReadOnlyAttribute(target);
				File.Delete(target);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot delete file " + target, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				RuntimeStatus = Status.Warning;
				return false;
			}
			return true;
		}

		static bool RemoveFolder(string folder) {
			if (Simulate||Restore) return true;

			if (!Directory.Exists(folder)) return true;
			try {
				RemoveReadOnlyAttribute(folder);
				Directory.Delete(folder, true);
			} catch (Exception ex) {
				LogMessage("[WARNING] Cannot delete folder " + folder, MsgType.MSG_WARNING);
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_WARNING);
				RuntimeStatus = Status.Warning;
				return false;
			}
			return true;
		}

		/*static void FastScanTree() {
			DriveInfo di = new DriveInfo("C:\\");
			NtfsReader reader = new NtfsReader(di, RetrieveMode.All);
			List<INode> nodes = reader.GetNodes("C:\\");
			foreach (INode node in nodes) {
			}

		}*/

		static void ScanTree(string sourceScanFile, string targetScanFile) {
			Thread sourceScanThread = null, targetScanThread = null;
			bool sourceAlive = false, targetAlive = false;
			string buf;
			bool sourceScanCompleted = false;
			bool targetScanCompleted = false;
			bool scanSource = false, scanTarget = false;

			LogMessage("[PAIR] Source: " + SourceRoot, MsgType.MSG_ALERT);
			LogMessage("[PAIR] Target: " + TargetRoot, MsgType.MSG_ALERT);
			MyXYConsole.WriteAt(SourceRootXY, SourceRoot, ConsoleColor.Cyan);
			MyXYConsole.WriteAt(TargetRootXY, TargetRoot, ConsoleColor.Cyan);
			LogMessage(String.Format("[PHASE] Scanning, Time = {0}", DateTime.Now.ToString()));
			MyXYConsole.WriteAt(PhaseXY, "SCAN", ConsoleColor.Green);
			PhaseTime.Start();
			scanSource = (!IgnoreSourceExist) && (!File.Exists(sourceScanFile));
			scanTarget = (!IgnoreTargetExist) && (!File.Exists(targetScanFile));
			if (IgnoreSourceExist) {
				sourceScanCompleted = true;
				MyXYConsole.WriteAt(SourcePathsXY, "Skip", ConsoleColor.Green);
				MyXYConsole.WriteAt(SourceFilesXY, "Skip", ConsoleColor.Green);
				MyXYConsole.WriteAt(SourceXY, "Skipped", ConsoleColor.Green);
			} else if (scanSource) {
				LogMessage("[INFO] Begin source folder scan");
			} else {
				LogMessage("[INFO] Load source scan file");
				if (LoadScan(sourceScanFile, out SourcePaths, out SourceTable)) {
					sourceScanCompleted = true;
					MyXYConsole.WriteAt(SourcePathsXY, SourcePaths.Count().ToString(), ConsoleColor.Green);
					MyXYConsole.WriteAt(SourceFilesXY, SourceTable.Count().ToString(), ConsoleColor.Green);
					MyXYConsole.WriteAt(SourceXY, "Loaded from scanfile", ConsoleColor.Green);
				} else {
					scanSource = true;  // scan file invalid, have to scan anyway
				}
			}
			if (IgnoreTargetExist) {
				targetScanCompleted = true;
				MyXYConsole.WriteAt(TargetPathsXY, "Skip", ConsoleColor.Green);
				MyXYConsole.WriteAt(TargetFilesXY, "Skip", ConsoleColor.Green);
				MyXYConsole.WriteAt(TargetXY, "Skipped", ConsoleColor.Green);
			} else if (scanTarget) {
				LogMessage("[INFO] Begin target folder scan");
			} else {
				LogMessage("[INFO] Load target scan file");
				if (LoadScan(targetScanFile, out TargetPaths, out TargetTable)) {
					targetScanCompleted = true;
					MyXYConsole.WriteAt(TargetPathsXY, TargetPaths.Count().ToString(), ConsoleColor.Green);
					MyXYConsole.WriteAt(TargetFilesXY, TargetTable.Count().ToString(), ConsoleColor.Green);
					MyXYConsole.WriteAt(TargetXY, "Loaded from scanfile", ConsoleColor.Green);
				} else {
					scanTarget = true;  // scan file invalid, have to scan anyway
				}
			}
			if (scanSource) {
				sourceScanThread = new Thread(() => Scan(SourceRoot, SourceRoot, SourcePaths, SourceTable, SourceXY, SourcePathsXY, SourceFilesXY, true));
				sourceAlive = true;
				sourceScanThread.Start();
			}
			if (scanTarget) {
				targetScanThread = new Thread(() => Scan(TargetRoot, TargetRoot, TargetPaths, TargetTable, TargetXY, TargetPathsXY, TargetFilesXY, false));
				targetAlive = true;
				targetScanThread.Start();
			}
			ScanCompleted = false;
			while (sourceAlive || targetAlive) {
				if (scanSource) {
					if (sourceAlive && !sourceScanThread.IsAlive) {
						if (ExitRequested) {
							MyXYConsole.WriteAt(SourceXY, "Scan Aborted", ConsoleColor.Green);
						} else {
							MyXYConsole.WriteAt(SourceXY, "Scan Complete", ConsoleColor.Green);
							sourceScanCompleted = true;
						}
						sourceAlive = false;
					}
				}
				if (scanTarget) {
					if (targetAlive && !targetScanThread.IsAlive) {
						if (ExitRequested) {
							MyXYConsole.WriteAt(TargetXY, "Scan Aborted", ConsoleColor.Green);
						} else {
							MyXYConsole.WriteAt(TargetXY, "Scan Complete", ConsoleColor.Green);
							targetScanCompleted = true;
						}
						targetAlive = false;
					}
				}
				Thread.Sleep(100);
			}
			ScanCompleted = sourceScanCompleted && targetScanCompleted;
			if ((scanSource & sourceScanCompleted)&&(!Simulate)) SaveScan(sourceScanFile, SourcePaths, SourceTable);
			if ((scanTarget & targetScanCompleted)&&(!Simulate)) SaveScan(targetScanFile, TargetPaths, TargetTable);
			buf = String.Format("[INFO] {0} source files in {1} directories", SourceTable.Count(), SourcePaths.Count());
			LogMessage(buf);
			buf = String.Format("[INFO] {0} target files in {1} directories", TargetTable.Count(), TargetPaths.Count());
			LogMessage(buf);
			LogMessage("[INFO] Time elapsed: " + TotalTime.Elapsed);
		}

		static void FindDupes(ILookup<string, FileTableItem> hashLookup, string dumpFile) {
			int i, items;
			ProgressPrinter progress = new ProgressPrinter(MyXYConsole, ProgressXY, PercentXY);
			TextWriter tw = null;
			int count = 0;

			items = hashLookup.Count();
			if (ExitRequested) return;
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
					if (itemList.ElementAt(0).MD5 == null) continue;    // exclude null MD5
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
			LogMessage("[INFO] Time elapsed: " + TotalTime.Elapsed);
		}

		static string TargetWrap(string filename) {
			return TargetRoot + "\\" + filename;
		}

		static string SourceWrap(string filename) {
			return SourceRoot + "\\" + filename;
		}

		static void MoveSync() {
			int items, i;
			//			string sourceStub, targetStub;
			bool matched;
			FileTableItem targetItem = new FileTableItem();
			ProgressPrinter progress = new ProgressPrinter(MyXYConsole, ProgressXY, PercentXY);
			List<SourceTargetPair> MoveList = new List<SourceTargetPair>();
			string buf;

			if (ExitRequested) return;
			PhaseTime.Restart();
			LogMessage(String.Format("[PHASE] Move sync, Time = {0}", DateTime.Now.ToString()));
			MyXYConsole.WriteAt(PhaseXY, "MOVES", ConsoleColor.Green);
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
							//if ((sourceItem.Size == item.Size) && (sourceItem.Timestamp == item.Timestamp)) {
							if (sourceItem.Size == item.Size) {	// if size & has are same, then it's the same file even if timestamp is different
								if (SourceByName[item.Filename].Count() > 0) continue;  // no move if file also exist in source
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
			MyXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0; i < items; i++) {
				if (ExitRequested) return;
				if (Restore) {
					buf = String.Format("[MOVE (Skip)] ({0}/{1}) {2}", i + 1, items, TargetWrap(MoveList[i].Source.Filename));
				} else {
					buf = String.Format("[MOVE] ({0}/{1}) {2}", i + 1, items, TargetWrap(MoveList[i].Source.Filename));
				}
				LogMessage(buf);
				LogMessage("   ==> " + TargetWrap(MoveList[i].Target.Filename), MsgType.MSG_INFO, false);
				MyXYConsole.WriteAt(SourceXY, MoveList[i].Source.Filename, ConsoleColor.Green);
				MyXYConsole.WriteAt(TargetXY, MoveList[i].Target.Filename, ConsoleColor.Green);
				if (MoveFile(TargetWrap(MoveList[i].Source.Filename), TargetWrap(MoveList[i].Target.Filename))) {
					TargetTable[MoveList[i].Source.Index].Filename = MoveList[i].Target.Filename;
				}
				MyXYConsole.WriteAt(IndexXY, (i + 1) + "/" + items, ConsoleColor.Magenta);
				progress.Print((100 * i) / items);
			}
			progress.Stop();
			LogMessage("[INFO] Time elapsed: " + TotalTime.Elapsed);
		}

		static bool ArraysEqual<T>(T[] a1, T[] a2) {
			if (ReferenceEquals(a1, a2))
				return true;

			if (a1 == null || a2 == null)
				return false;

			if (a1.Length != a2.Length)
				return false;

			var comparer = EqualityComparer<T>.Default;
			for (int i = 0; i < a1.Length; i++) {
				if (!comparer.Equals(a1[i], a2[i])) return false;
			}
			return true;
		}

		static void CopySync() {
			int items, i;
			bool matched;
			FileTableItem targetItem = new FileTableItem();
			ProgressPrinter progress = new ProgressPrinter(MyXYConsole, ProgressXY, PercentXY);
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

			if (ExitRequested) return;
			PhaseTime.Restart();
			LogMessage(String.Format("[PHASE] Copy sync, Time = {0}", DateTime.Now.ToString()));
			MyXYConsole.WriteAt(PhaseXY, "COPY", ConsoleColor.Green);
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
					if (sourceItem.Size != targetItem.Size) {
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						pair.UpdateReason = Reason.Size;
						UpdateList.Add(pair);
					} else if ((!ArraysEqual<byte>(sourceItem.MD5, targetItem.MD5)) ||
						((sourceItem.MD5 != null) && (targetItem.MD5 == null))) {
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						pair.UpdateReason = Reason.MD5;
						UpdateList.Add(pair);
					} else if (sourceItem.Timestamp.CompareTo(targetItem.Timestamp) < 0) {
						// if timestamps are different, just touch
						// dest file is newer
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						pair.UpdateReason = Reason.Timestamp;
						TouchList.Add(pair);
						RuntimeStatus = Status.Warning;
					} else if (sourceItem.Timestamp.CompareTo(targetItem.Timestamp) > 0) {
						// dest file is older
						SourceTargetPair pair = new SourceTargetPair();
						pair.Source = sourceItem;
						pair.Target = targetItem;
						pair.UpdateReason = Reason.Timestamp;
						if (Retouch) TouchList.Add(pair);
						else UpdateList.Add(pair);
						//	updateSum = updateSum + sourceItem.Size;
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
			LogMessage("[COPYSYNC] " + TouchList.Count() + " dest files have newer timestamp");
			LogMessage("[COPYSYNC] " + UpdateList.Count(a => a.UpdateReason == Reason.Timestamp) + " files to update with newer timestamp");
			LogMessage("[COPYSYNC] " + UpdateList.Count(a => a.UpdateReason == Reason.Size) + " files to update with different file size");
			LogMessage("[COPYSYNC] " + UpdateList.Count(a => a.UpdateReason == Reason.MD5) + " files to update with different MD5");
			LogMessage("[COPYSYNC] " + CopyList.Count() + " files to copy");
			LogMessage("[COPYSYNC] Touching (if enabled)...");
			items = TouchList.Count();
			for (i = 0; i < items; i++) {
				if (ExitRequested) return;
				LogMessage("[WARNING] Destination file has different timestamp: " + TouchList[i].Target.Filename, MsgType.MSG_WARNING);
				if (Retouch) {
					try {
						if (!(Simulate||Restore)) {
							File.SetLastWriteTime(TargetWrap(TouchList[i].Target.Filename), TouchList[i].Source.Timestamp);
						}
						MyXYConsole.WriteAt(TargetXY, TouchList[i].Target.Filename, ConsoleColor.Green);
						if (Restore) {
							LogMessage("[TOUCH (Skip)] Target timestamp updated");
						} else {
							LogMessage("[TOUCH] Target timestamp updated");
						}
						TargetTable[TouchList[i].Target.Index].Timestamp = SourceTable[TouchList[i].Source.Index].Timestamp;
					} catch (IOException ex) {
						buf = ex.Message;
						TextFgColor(System.ConsoleColor.Red);
						LogMessage("[WARNING] Target timestamp update unsuccessful", MsgType.MSG_WARNING);
					}
				}
			}
			MyXYConsole.WriteAt(PhaseXY, "UPDATE", ConsoleColor.Green);
			runningSum = 0;
			phaseStart = PhaseTime.Elapsed;
			progress.Start();
			buf = String.Format("[COPYSYNC] {0} files, {1} to update", UpdateList.Count(), EncodeByteSize(updateSum));
			LogMessage(buf);
			LogMessage("[COPYSYNC] Updating...");
			items = UpdateList.Count();
			MyXYConsole.WriteAt(ByteXY, "0/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
			MyXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0; i < items; i++) {
				if (ExitRequested) return;
				if (Restore) {
					buf = String.Format("[UPDATE (Skip)] ({0}/{1}) {2} ({3})", i + 1, items, TargetWrap(UpdateList[i].Target.Filename), UpdateList[i].UpdateReason.ToString());
				} else {
					buf = String.Format("[UPDATE] ({0}/{1}) {2} ({3})", i + 1, items, TargetWrap(UpdateList[i].Target.Filename), UpdateList[i].UpdateReason.ToString());
				}
				LogMessage(buf);
				LogMessage(UpdateList[i].Source.Timestamp.ToShortDateString() + " " + UpdateList[i].Source.Timestamp.ToShortTimeString() + " <> " +
							UpdateList[i].Target.Timestamp.ToShortDateString() + " " + UpdateList[i].Target.Timestamp.ToShortTimeString(), MsgType.MSG_INFO, false);
				MyXYConsole.WriteAt(SourceXY, SourceWrap(UpdateList[i].Source.Filename), ConsoleColor.Green);
				MyXYConsole.WriteAt(TargetXY, TargetWrap(UpdateList[i].Target.Filename), ConsoleColor.Green);
				if (!(Simulate || Restore)) {
					if (CopyFile(SourceWrap(UpdateList[i].Source.Filename), TargetWrap(UpdateList[i].Target.Filename))) {
						TargetTable[UpdateList[i].Target.Index] = SourceTable[UpdateList[i].Source.Index];
						TargetTable[UpdateList[i].Target.Index].Index = UpdateList[i].Target.Index;
					}
				}
				runningSum += UpdateList[i].Source.Size;
				MyXYConsole.WriteAt(ByteXY, EncodeByteSize(runningSum) + "/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
				MyXYConsole.WriteAt(IndexXY, (i + 1) + "/" + (UpdateList.Count() + CopyList.Count()), ConsoleColor.Magenta);
				elapsed = PhaseTime.Elapsed - phaseStart;
				totalTicks = (long)(((double)(updateSum + copySum) / (double)(runningSum)) * elapsed.Ticks);
				ETA = new TimeSpan(totalTicks - elapsed.Ticks);
				MyXYConsole.WriteAt(EstimateXY, ETA.ToString(), ConsoleColor.Cyan);
				if (updateSum > 0) {
					progress.Print((int)((100 * runningSum) / updateSum));
				}
			}
			progress.Stop();
			buf = String.Format("[COPYSYNC] {0} transferred during update phase", EncodeByteSize(updateSum));
			LogMessage(buf);
			MyXYConsole.WriteAt(PhaseXY, "COPY", ConsoleColor.Green);
			runningSum = 0;
			progress.Start();
			buf = String.Format("[COPYSYNC] {0} files, {1} to copy", CopyList.Count(), EncodeByteSize(copySum));
			LogMessage(buf);
			LogMessage("[COPYSYNC] Copying...");
			items = CopyList.Count();
			MyXYConsole.WriteAt(ByteXY, "0/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
			MyXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0; i < items; i++) {
				if (ExitRequested) return;
				buf = String.Format("[COPY] ({0}/{1}) {2}", i + 1, items, SourceWrap(CopyList[i].Source.Filename));
				LogMessage(buf);
				LogMessage("   ==> " + TargetWrap(CopyList[i].Target.Filename), MsgType.MSG_INFO, false);
				MyXYConsole.WriteAt(SourceXY, SourceWrap(CopyList[i].Source.Filename), ConsoleColor.Green);
				MyXYConsole.WriteAt(TargetXY, TargetWrap(CopyList[i].Target.Filename), ConsoleColor.Green);
				if (!Simulate) {
					if (CopyFile(SourceWrap(CopyList[i].Source.Filename), TargetWrap(CopyList[i].Target.Filename))) {
						TargetTable.Add(SourceTable[CopyList[i].Source.Index]);
						TargetTable[TargetTable.Count() - 1].Index = TargetTable.Count() - 1;
					}
				}
				runningSum += CopyList[i].Source.Size;
				MyXYConsole.WriteAt(ByteXY, EncodeByteSize(updateSum + runningSum) + "/" + EncodeByteSize(updateSum + copySum), ConsoleColor.Magenta);
				MyXYConsole.WriteAt(IndexXY, (i + UpdateList.Count() + 1) + "/" + (UpdateList.Count() + CopyList.Count()), ConsoleColor.Magenta);
				elapsed = PhaseTime.Elapsed - phaseStart;
				totalTicks = (long)(((double)(updateSum + copySum) / (double)(runningSum + updateSum)) * elapsed.Ticks);
				ETA = new TimeSpan(totalTicks - elapsed.Ticks);
				MyXYConsole.WriteAt(EstimateXY, ETA.ToString(), ConsoleColor.Cyan);
				if (copySum > 0) {
					progress.Print((int)((100 * runningSum) / copySum));
				}
			}
			progress.Stop();
			buf = String.Format("[COPYSYNC] {0} transferred during copy phase", EncodeByteSize(copySum));
			LogMessage(buf);
			LogMessage("[INFO] Time elapsed: " + TotalTime.Elapsed);
		}

		static void DeleteSync() {
			int items, i, j;
			string targetStub;
			bool matched;
			//			string sourcePath;
			ProgressPrinter progress = new ProgressPrinter(MyXYConsole, ProgressXY, PercentXY);
			List<FileTableItem> DeleteList = new List<FileTableItem>();
			string buf;
			long deleteSum;

			if (ExitRequested) return;
			PhaseTime.Restart();
			LogMessage(String.Format("[PHASE] Delete sync, Time = {0}", DateTime.Now.ToString()));
			MyXYConsole.WriteAt(PhaseXY, "DELETE", ConsoleColor.Green);
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
			deleteSum = DeleteList.Sum(item => item.Size);
			progress.Stop();
			buf = String.Format("[DELSYNC] {0} files, {1} to delete", DeleteList.Count(), EncodeByteSize(deleteSum));
			LogMessage(buf);
			LogMessage("[DELSYNC] Deleting...");
			progress.Start();
			items = DeleteList.Count();
			MyXYConsole.WriteAt(IndexXY, "0/0", ConsoleColor.Magenta);
			for (i = 0; i < items; i++) {
				if (ExitRequested) return;
				progress.Print((100 * i) / items);
				if (Restore) {
					buf = String.Format("[DELETE (Skip)] ({0}/{1}) {2}", i + 1, items, TargetWrap(DeleteList[i].Filename));
				} else {
					buf = String.Format("[DELETE] ({0}/{1}) {2}", i + 1, items, TargetWrap(DeleteList[i].Filename));
				}
				LogMessage(buf);
				MyXYConsole.WriteAt(TargetXY, TargetWrap(DeleteList[i].Filename), ConsoleColor.Green);
				if (!(Simulate || Restore)) {
					if (DeleteFile(TargetWrap(DeleteList[i].Filename))) {
						TargetTable.Remove(DeleteList[i]);
					}
				}
				MyXYConsole.WriteAt(IndexXY, (i + 1) + "/" + items, ConsoleColor.Magenta);
			}
			progress.Stop();
			// re-index TargetTable after element deletion
			items = TargetTable.Count();
			for (i = 0; i < items; i++) {
				TargetTable[i].Index = i;
			}
			LogMessage("[DELSYNC] Removing trees...");
			progress.Start();
			items = TargetPaths.Count();
			string[] sourceStubs = new string[SourcePaths.Count];
			for (i = 0; i < SourcePaths.Count; i++) {
				sourceStubs[i] = StripRoot(SourceRoot, SourcePaths[i]);
			}
			for (i = items - 1; i >= 0; i--) {
				if (ExitRequested) return;
				progress.Print((100 * (items - i)) / items);
				targetStub = StripRoot(TargetRoot, TargetPaths[i]);
				matched = false;
				/*foreach (string sourcePath in SourcePaths) {
					sourceStub = StripRoot(sourceRoot, sourcePath);
					if (sourceStub == targetStub) {
						matched = true;
						break;
					}
				}*/
				for (j = 0; j < sourceStubs.Length; j++) {
					if (sourceStubs[j] == targetStub) {
						matched = true;
						break;
					}
				}
				if (!matched) {
					if (Restore) {
						LogMessage("[DELTREE (Skip)] " + TargetPaths[i]);
					} else {
						LogMessage("[DELTREE] " + TargetPaths[i]);
					}
					MyXYConsole.WriteAt(TargetXY, TargetPaths[i], ConsoleColor.Green);
					if (!(Simulate || Restore)) {
						if (RemoveFolder(TargetPaths[i])) {
							TargetPaths.RemoveAt(i);
						}
					}
				}
			}
			progress.Stop();
			buf = String.Format("[DELSYNC] {0} total deleted", EncodeByteSize(deleteSum));
			LogMessage("[INFO] Time elapsed: " + TotalTime.Elapsed);
		}

		static string GetFolderName(string path) {
			char[] delims = new char[] { '\\' };
			string[] tokens;
			tokens = path.Split(delims, StringSplitOptions.RemoveEmptyEntries);
			return tokens[tokens.Count() - 1];
		}

		static void LoadProject(string prjpath) {
			ProjectMode mode = ProjectMode.Seek;
			ExcludeNameList = new List<string>();
			ExcludePathList = new List<string>();
			IgnoreNameList = new List<string>();
			IgnorePathList = new List<string>();
			MatchNameList = new List<string>();
			MatchPathList = new List<string>();
			Sources = new List<string>();
			Targets = new List<string>();
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
				} else if ((buf == "[Exclude]") || (buf == "[Skip]")) {
					mode = ProjectMode.Exclude;
				} else if (buf == "[Ignore]") {
					mode = ProjectMode.Ignore;
				} else if (buf == "[Network]") {
					mode = ProjectMode.Network;
					//DoNetwork = true;
				} else if (buf == "[Log]") {
					mode = ProjectMode.Log;
					//DoLog = true;
				} else if (mode == ProjectMode.Path) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "source":
								if (Restore) {
									Targets.Add(FixRootDir(tokens[1]));
								} else {
									Sources.Add(FixRootDir(tokens[1]));
								}
								LogMessage("[SOURCE] Root #" + Sources.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "target":
								if (Restore) {
									Sources.Add(FixRootDir(tokens[1]));
								} else {
									Targets.Add(FixRootDir(tokens[1]));
								}
								LogMessage("[TARGET] Root #" + Targets.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
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
				} else if (mode == ProjectMode.Exclude) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "path":
								ExcludePathList.Add(tokens[1]);
								LogMessage("[EXCLUDE] Path #" + ExcludePathList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "name":
								ExcludeNameList.Add(tokens[1]);
								LogMessage("[EXCLUDE] Name #" + ExcludeNameList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				} else if (mode == ProjectMode.Ignore) {
					tokens = buf.Split(delims, StringSplitOptions.RemoveEmptyEntries);
					if (tokens.Count() > 0) {
						tokens[1] = tokens[1].Trim();
						switch (tokens[0].ToLower().Trim()) {
							case "path":
								IgnorePathList.Add(tokens[1]);
								LogMessage("[IGNORE] Path #" + IgnorePathList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
								break;
							case "name":
								IgnoreNameList.Add(tokens[1]);
								LogMessage("[IGNORE] Name #" + IgnoreNameList.Count.ToString() + ": " + tokens[1], MsgType.MSG_ALERT);
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
								MailTo = tokens[1];
								LogMessage("[NETWORK] MailTo: " + MailTo, MsgType.MSG_ALERT);
								break;
							case "from":
								MailFrom = tokens[1];
								LogMessage("[NETWORK] MailFrom: " + MailFrom, MsgType.MSG_ALERT);
								break;
							case "server":
								MailServer = tokens[1];
								LogMessage("[NETWORK] MailServer: " + MailServer, MsgType.MSG_ALERT);
								break;
							case "port":
								NetPort = Convert.ToInt32(tokens[1]);
								LogMessage("[NETWORK] port: " + NetPort, MsgType.MSG_ALERT);
								break;
							case "user":
								NetUser = tokens[1];
								LogMessage("[NETWORK] userid: " + NetUser, MsgType.MSG_ALERT);
								break;
							case "pass":
								NetPass = tokens[1];
								LogMessage("[NETWORK] passwd: " + NetPass, MsgType.MSG_ALERT);
								break;
							case "ssl":
								if ((tokens[1] == "true") || (tokens[1] == "yes")) {
									DoSsl = true;
								} else {
									DoSsl = false;
								}
								LogMessage("[NETWORK] SSL: " + (DoSsl ? "Yes" : "No"), MsgType.MSG_ALERT);
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
								LogPath = tokens[1];
								LogMessage("[LOG] path: " + LogPath, MsgType.MSG_ALERT);
								break;
							case "attach":
								if (tokens[1].ToLower() == "yes") {
									AttachLog = true;
								}
								LogMessage("[LOG] attach: " + AttachLog, MsgType.MSG_ALERT);
								break;
							case "autosave":
								AutoSaveInterval = Int32.Parse(tokens[1]);
								LogMessage("[LOG] autosave: " + AutoSaveInterval + " mins", MsgType.MSG_ALERT);
								break;
							default: break;
						}
					}
				}
			}
			tr.Close();
			if (MatchNameList.Count() > 0) MatchNameValid = true;
			if (MatchPathList.Count() > 0) MatchPathValid = true;
			if (LogPath != null) DoLog = true;
			if (MailServer != null) DoNetwork = true;
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
			} catch (ArgumentNullException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			} catch (ObjectDisposedException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			} catch (SmtpFailedRecipientsException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
			} catch (SmtpException ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				//				LogMessage(ex.InnerException.Message, MsgType.MSG_ERROR);
			} catch (Exception ex) {
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
			} catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				CleanExit();
			}
		}

		static void CleanExit() {
			RuntimeStatus = Status.Error;
			Notify(RuntimeStatus);
			System.Console.ResetColor();
			Environment.Exit(1);
		}

		static void CheckProject() {
			int i;
			SourceTargetList = new List<SourceTargetPath>();

			if (Sources.Count == 0) {
				LogMessage("[ERROR] No Source root specified", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			}
			if (Targets.Count == 0) {
				LogMessage("[ERROR] No Target root specified", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			}
			if (Sources.Count != Targets.Count) {
				LogMessage("[ERROR] Source and Target root # mismatch", MsgType.MSG_ERROR);
				LogMessage("Exiting...", MsgType.MSG_ERROR);
				CleanExit();
			} else {
				for (i = 0; i < Sources.Count; i++) {
					SourceTargetPath pair = new SourceTargetPath();
					pair.Source = Sources[i];
					pair.Target = Targets[i];
					SourceTargetList.Add(pair);
				}
			}
			if (!IgnoreSourceExist) {
				foreach (string path in Sources) {
					if (!Directory.Exists(path)) {
						LogMessage("[ERROR] Source dir not found: " + path, MsgType.MSG_ERROR);
						LogMessage("Exiting...", MsgType.MSG_ERROR);
						CleanExit();
					}
				}
			}
			if (!IgnoreTargetExist) {
				foreach (string path in Targets) {
					if (!Directory.Exists(path)) {
						LogMessage("[ERROR] Target dir not found: " + path, MsgType.MSG_ERROR);
						LogMessage("Exiting...", MsgType.MSG_ERROR);
						CleanExit();
					}
				}
			}
		}

		static void Notify(Status statusCode) {
			string logfile;
			string datepat = @"yyyy-MM-dd HH-mm-ss tt";
			string status;
			string logbody;
			string mailbody;

			switch (statusCode) {
				case Status.Success: status = " [Success]"; break;
				case Status.Warning: status = " [Warning]"; break;
				case Status.Error: status = " [Error]"; break;
				default: status = " [Success]"; break;
			}
			LogMessage(String.Format("[INFO] Notifying, Time = {0}", DateTime.Now.ToString()));
			LogMessage("[INFO] Logfile size: " + LogBuilder.Length + " bytes");
			if (DoNetwork) {
				LogMessage("[INFO] Sending notification email");
				if (AttachLog) {
					try {
						mailbody = MailBuilder.ToString();
						SendMail(MailServer, NetPort, DoSsl, NetUser, NetPass, MailFrom, MailTo, "cSync " + ProjectFile + status, mailbody);
					} catch (OutOfMemoryException ex) {
						LogMessage("[ERROR] " + ex.Message);
					}
				} else {
					SendMail(MailServer, NetPort, DoSsl, NetUser, NetPass, MailFrom, MailTo, "cSync " + ProjectFile + status, "See Log File for details");
				}
			}
			if (DoLog) {
				logfile = LogPath + "\\csync " + Path.GetFileNameWithoutExtension(ProjectFile) + " " + DateTime.Now.ToString(datepat) + status + ".log";
				LogMessage("[INFO] Saving log file " + logfile);
				try {
					logbody = LogBuilder.ToString();
					SaveLog(logfile, logbody);
				} catch (OutOfMemoryException ex) {
					LogMessage("[ERROR] " + ex.Message);
				}
			}
			System.Console.ResetColor();
		}

		static void OnProcessExit(object sender, EventArgs e) {

		}

		static void PrintHelp() {
			MyXYConsole.AddLog("cSync " + Version + " - (C)2002-2024 Bo-Yi Lin", ConsoleColor.Red);
			MyXYConsole.AddLog("syntax: cSync -p [prjpath] -l verbosity [-t -s -fs -ft -is -it -sim -res]", ConsoleColor.Red);
			MyXYConsole.AddLog("verbosity: INFO, WARNING, ERROR", ConsoleColor.Red);
			MyXYConsole.AddLog("-t: touch newer dest file to source timestamp", ConsoleColor.Red);
			MyXYConsole.AddLog("-s: scan only, to generate scanfiles", ConsoleColor.Red);
			MyXYConsole.AddLog("-fs: force source re-scan", ConsoleColor.Red);
			MyXYConsole.AddLog("-ft: force target re-scan", ConsoleColor.Red);
			MyXYConsole.AddLog("-is: ignore source existence", ConsoleColor.Red);
			MyXYConsole.AddLog("-it: ignore target existence", ConsoleColor.Red);
			MyXYConsole.AddLog("-sim: simulate only, don't actually write or delete", ConsoleColor.Red);
			MyXYConsole.AddLog("-res: restore mode, restores source files from target (experimental)", ConsoleColor.Red);
		}

		static void PrintTime() {
			TimeSpan oldphase = new TimeSpan(0);
			TimeSpan oldtotal = new TimeSpan(0);
			while ((PhaseTime != null) && (TotalTime != null) && (!AppExitCondition)) {
				if (PhaseTime.Elapsed != oldphase) {
					MyXYConsole.WriteAt(PhaseTimeXY, PhaseTime.Elapsed.ToString(), ConsoleColor.Cyan);
					oldphase = PhaseTime.Elapsed;
				}
				if (TotalTime.Elapsed != oldtotal) {
					MyXYConsole.WriteAt(TotalTimeXY, TotalTime.Elapsed.ToString(), ConsoleColor.Cyan);
					oldtotal = TotalTime.Elapsed;
				}
				if (Console.KeyAvailable) {
					if (Console.ReadKey(false).Key == ConsoleKey.Escape) {
						LogMessage("[WARNING] Exit requested", MsgType.MSG_WARNING);
						RuntimeStatus = Status.Warning;
						ExitRequested = true;
					}
				}
				Thread.Sleep(250);
			}
		}

		static void AutoSaver() {
			int interval = AutoSaveInterval * 60 * 4;
			while ((AutoSaveInterval > 0) && (!AppExitCondition)) {
				LogMessage("[INFO] AutoSaving log file " + AutosaveLogFile);
				SaveLog(AutosaveLogFile, LogBuilder.ToString());
				for (int i = 0; i < interval; i++) {
					Thread.Sleep(250);
					if (AppExitCondition) break;
				}
				//Thread.Sleep(AutoSaveInterval * 1000 * 60);
			}
		}

		static void SaveScan(string filename, List<string> paths, List<FileTableItem> table) {
			BinaryWriter bw;

			LogMessage("[INFO] Save scan data to " + filename);
			try {
				bw = new BinaryWriter(File.Open(filename, FileMode.Create));
			} catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				return;
			}
			int ptr = 0;
			int encodedLen;
			byte[] hdr;
			byte[] blob;
			hdr = new byte[8];
			Header.IntToByteArray(paths.Count, hdr, 0);
			Header.IntToByteArray(table.Count, hdr, 4);
			bw.Write(hdr);	// first 8 bytes are the path and table counts
			Header.CharToByteArray(new char[] { 'P', 'A', 'T', 'H' }, hdr, 0);
			foreach (string path in paths) {
				ptr = 0;
				encodedLen = Header.GetEncodedStringLen(path);
				Header.IntToByteArray(encodedLen, hdr, 4);
				blob = new byte[encodedLen];
				Header.StringToByteArray(path, blob, ref ptr);
				bw.Write(hdr);
				bw.Write(blob);
			}
			Header.CharToByteArray(new char[] { 'I', 'T', 'E', 'M' }, hdr, 0);
			foreach (FileTableItem item in table) {
				blob = item.Serialize();
				Header.IntToByteArray(blob.Length, hdr, 4);
				bw.Write(hdr);
				bw.Write(blob);
			}
			//bf.Serialize(fs, paths);
			//bf.Serialize(fs, table);
			bw.Close();
			MyMD5.Detach(filename, true);
			MyMD5.Attach(filename);
		}

		static bool LoadScan(string filename, out List<string> paths, out List<FileTableItem> table) {
			BinaryReader br;
			paths = new List<string>();
			table = new List<FileTableItem>();

			if (!MyMD5.Verify(filename)) {
				LogMessage("[WARNING] MD5 mismatch in " + filename, MsgType.MSG_WARNING);
				paths = new List<string>();
				table = new List<FileTableItem>();
				return false;
			}
			try {
				br = new BinaryReader(File.Open(filename, FileMode.Open));
			} catch (Exception ex) {
				LogMessage("[ERROR] " + ex.Message, MsgType.MSG_ERROR);
				paths = new List<string>();
				table = new List<FileTableItem>();
				return false;
			}
			//paths = (List<string>)bf.Deserialize(fs);
			//table = (List<FileTableItem>)bf.Deserialize(fs);
			int i, len, ptr;
			byte[] hdr;
			byte[] blob;
			char[] pat;
			hdr = br.ReadBytes(8);
			int pathCount = Header.ByteArrayToInt(hdr, 0);
			int tableCount = Header.ByteArrayToInt(hdr, 4);
			pat = new char[] { 'P', 'A', 'T', 'H' };
			ptr = 0;
			for (i = 0; i < pathCount; i++) {
				ptr = 0;
				hdr = br.ReadBytes(8);
				if (Header.CheckHeader(pat, hdr, 0)) {
					len = Header.ByteArrayToInt(hdr, 4);
					blob = br.ReadBytes(len);
					paths.Add(Header.ByteArrayToString(blob, ref ptr));
				} else {
					LogMessage("[ERROR] Error reading scanfile, possible corruption", MsgType.MSG_ERROR);
					br.Close();
					return false;
				}
			}
			pat = new char[] { 'I', 'T', 'E', 'M' };
			ptr = 0;
			FileTableItem item;
			for (i = 0; i < tableCount; i++) {
				hdr = br.ReadBytes(8);
				if (Header.CheckHeader(pat, hdr, 0)) {
					item = new FileTableItem();
					len = Header.ByteArrayToInt(hdr, 4);
					blob = br.ReadBytes(len);
					item.Deserialize(blob);
					table.Add(item);
				} else {
					LogMessage("[ERROR] Error reading scanfile, possible corruption", MsgType.MSG_ERROR);
					br.Close();
					return false;
				}
			}
			br.Close();
			return true;
		}

		static void Main(string[] args) {
			int i, argn;
			string dupefile = null;
			string sourceScanFile, targetScanFile;

			MyMD5.LogCallBack = LogCallBackHandler; 
			MyXYConsole = new XYConsole(25, MessageXY, 12);
			Console.Clear();
			MyXYConsole.PrintRaw(0, 0, Template, ConsoleColor.Yellow);
			MyXYConsole.WriteAt(VersionXY, "csync " + Version, ConsoleColor.Cyan);
			SourcePaths = new List<string>();
			TargetPaths = new List<string>();
			SourceTable = new List<FileTableItem>();
			TargetTable = new List<FileTableItem>();
			TotalTime = new Stopwatch();
			PhaseTime = new Stopwatch();
			TotalTime.Start();
			TimeThread = new Thread(PrintTime);
			TimeThread.Start();
			if (args.Length == 0) {
				MyXYConsole.Finish(); PhaseTime = null; TotalTime = null;
				LogMessage("[ERROR] No arguments specified", MsgType.MSG_ERROR, true);
				PrintHelp();
				CleanExit();
			}
			MsgLevel = MsgType.MSG_INFO;
			AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			for (argn = 0; argn < args.Length; argn++) {
				if (args[argn] == "-p") ProjectFile = args[argn + 1];
				if (args[argn] == "-l") {
					switch (args[argn + 1].ToLower()) {
						case "info": MsgLevel = MsgType.MSG_INFO; break;
						case "alert": MsgLevel = MsgType.MSG_ALERT; break;
						case "warning": MsgLevel = MsgType.MSG_WARNING; break;
						case "error": MsgLevel = MsgType.MSG_ERROR; break;
						default: break;
					}
				}
				if (args[argn] == "-t") {
					Retouch = true;
				}
				if (args[argn] == "-s") {
					ScanOnly = true;
				}
				if (args[argn] == "-fs") {
					ForceSourceScan = true;
				}
				if (args[argn] == "-ft") {
					ForceTargetScan = true;
				}
				if (args[argn] == "-is") {
					IgnoreSourceExist = true;
				}
				if (args[argn] == "-it") {
					IgnoreTargetExist = true;
				}
				if (args[argn] == "-sim") {
					Simulate = true;
				}
				if (args[argn] == "-res") {
					Restore = true;
				}
			}
			if (ProjectFile == "") {
				MyXYConsole.Finish(); PhaseTime = null; TotalTime = null;
				LogMessage("[ERROR] No project file specified", MsgType.MSG_ERROR, true);
				PrintHelp();
				CleanExit();
			}
			LoadProject(ProjectFile);
			CheckProject();
			if (DoLog && (AutoSaveInterval > 0)) {
				AutosaveLogFile = LogPath + "\\" + Path.GetFileNameWithoutExtension(ProjectFile) + " " + DateTime.Now.ToString(@"yyyy-MM-dd HH-mm-ss tt") + "_autosave.log";
				AutoSaveThread = new Thread(AutoSaver);
				AutoSaveThread.Start();
			}
			MyXYConsole.WriteAt(ProjectXY, ProjectFile, ConsoleColor.Cyan);
			for (i = 0; i < SourceTargetList.Count; i++) {
				if (ExitRequested) break;
				SourcePaths.Clear();
				TargetPaths.Clear();
				SourceTable.Clear();
				TargetTable.Clear();
				SourceRoot = SourceTargetList[i].Source;
				TargetRoot = SourceTargetList[i].Target;
				sourceScanFile = SourceRoot + "\\" + Path.GetFileNameWithoutExtension(ProjectFile) + ".scan";
				targetScanFile = TargetRoot + "\\" + Path.GetFileNameWithoutExtension(ProjectFile) + ".scan";
				if (ForceSourceScan) DeleteFile(sourceScanFile);
				if (ForceTargetScan) DeleteFile(targetScanFile);
				ScanTree(sourceScanFile, targetScanFile);
				if (!ScanOnly) {
					SourceByName = SourceTable.ToLookup(t => t.Filename);
					SourceByHash = SourceTable.ToLookup(t => t.MD5string);
					TargetByName = TargetTable.ToLookup(t => t.Filename);
					TargetByHash = TargetTable.ToLookup(t => t.MD5string);
					if (DoLog) dupefile = LogPath + "\\csync " + Path.GetFileNameWithoutExtension(ProjectFile) + " dupes.txt";
					else dupefile = SourceRoot + "\\dupes.txt";
					FindDupes(SourceByHash, dupefile);
					MoveSync();
					TargetByName = TargetTable.ToLookup(t => t.Filename);   // have to re-do lookup after movesync
					TargetByHash = TargetTable.ToLookup(t => t.MD5string);
					DeleteSync();
					TargetByName = TargetTable.ToLookup(t => t.Filename);   // have to re-do lookup after movesync
					TargetByHash = TargetTable.ToLookup(t => t.MD5string);
					CopySync();
					// update changes on target to scan file
					if (!(IgnoreTargetExist||Simulate)) SaveScan(targetScanFile, TargetPaths, TargetTable);
				}
			}
			MyXYConsole.WriteAt(PhaseXY, "COMPLETE", ConsoleColor.Green);
			Notify(RuntimeStatus);
			AppExitCondition = true;
			//TimeThread.Abort();
			while (TimeThread.IsAlive) {
				Thread.Sleep(100);
			}
			if (AutoSaveThread != null) {
				while (AutoSaveThread.IsAlive) {
					Thread.Sleep(100);
				}
				//AutoSaveThread.Abort();
				File.Delete(AutosaveLogFile);
			}
			TotalTime.Stop();
			PhaseTime.Stop();
			TotalTime = null;
			PhaseTime = null;
			LogMessage(String.Format("[INFO] Exit, Time = {0}", DateTime.Now.ToString()));
			MyXYConsole.Finish();
			System.Console.ResetColor();
			//			Console.OutputEncoding = System.Text.Encoding.Default;
		}
	}
}
