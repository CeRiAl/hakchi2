using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace com.clusterrr.hakchi_gui
{
    #region Enums

    public enum CompressionLevel
    {
        None   = 0,
        Fast   = 1,
        Low    = 3,
        Normal = 5,
        High   = 7,
        Ultra  = 9
    }
    #endregion

    #region Interfaces

    public interface ISevenZipCompressor
    {
        CompressionLevel CompressionLevel { get; set; }
        void CompressFiles(string archiveName, params string[] fileFullNames);
    }

    public interface ISevenZipExtractor : IDisposable
    {
        ReadOnlyCollection<string> ArchiveFileNames { get; }
        void ExtractArchive(string directory);
        void ExtractFile(string fileName, Stream stream);
        void ExtractFile(int index, Stream stream);
    }
    #endregion

    #region Wrapper classes

    public class SevenZipWrapper
    {
    	static protected readonly bool runningOnUnix = Environment.OSVersion.Platform.Equals(PlatformID.Unix);
    }

    public class SevenZipCompressor : SevenZipWrapper, ISevenZipCompressor
    {
        static Type compressorType = (runningOnUnix) ? typeof(SevenZipCompressorUnix) : typeof(SevenZipCompressorWindows);
        ISevenZipCompressor compressor = (ISevenZipCompressor) Activator.CreateInstance(compressorType);

        public CompressionLevel CompressionLevel
        {
            get { return compressor.CompressionLevel; }
            set { compressor.CompressionLevel = value; }
        }

        public void CompressFiles(string archiveName, params string[] fileFullNames) =>
            compressor.CompressFiles(archiveName, fileFullNames);
    }

    public class SevenZipExtractor : SevenZipWrapper, ISevenZipExtractor
    {
        static Type extractorType = (runningOnUnix) ? typeof(SevenZipExtractorUnix) : typeof(SevenZipExtractorWindows);
        ISevenZipExtractor extractor;

        public SevenZipExtractor(string fullName)
        {
            extractor = (ISevenZipExtractor) Activator.CreateInstance(extractorType, fullName);
        }

        public SevenZipExtractor(Stream stream)
        {
            extractor = (ISevenZipExtractor) Activator.CreateInstance(extractorType, stream);
        }

        public ReadOnlyCollection<string> ArchiveFileNames { get { return extractor.ArchiveFileNames; } }

        public void ExtractArchive(string directory) => extractor.ExtractArchive(directory);
        public void ExtractFile(int index, Stream stream) => extractor.ExtractFile(index, stream);
        public void ExtractFile(string fileName, Stream stream) => extractor.ExtractFile(fileName, stream);
        public void Dispose() => extractor.Dispose();
    }
    #endregion

    #region Implementations for Unix/Linux

    class SevenZipUnix
    {
        const string SEVENZIP_BINARY = "7z";

        protected static Process runSevenZip(string arguments) =>
            Process.Start(new ProcessStartInfo(SEVENZIP_BINARY, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
    }

    class SevenZipCompressorUnix : SevenZipUnix, ISevenZipCompressor
    {
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Normal;
        
        public void CompressFiles(string archiveName, params string[] fileFullNames)
        {
            string fileNamesParam = String.Join(" ", fileFullNames.Select(fileFullName => $"\"{fileFullName}\"").ToArray());
            runSevenZip($"a -mx{(int)CompressionLevel} -y \"{archiveName}\" {fileNamesParam}");
        }
    }

    class SevenZipExtractorUnix : SevenZipUnix, ISevenZipExtractor
    {
        string archiveFullName;
        bool archiveIsTemporary = false;
        List<ArchiveEntry> archiveEntries = new List<ArchiveEntry>();

        public ReadOnlyCollection<string> ArchiveFileNames
        {
            get
            {
                return new ReadOnlyCollection<string>(archiveEntries.Select(entry => entry.get("Path")).ToList());
            }
        }

        public SevenZipExtractorUnix(string fullName)
        {
            readArchiveEntries(fullName);
        }

        public SevenZipExtractorUnix(Stream stream)
        {
            string tempFileName = Path.GetTempFileName();
            byte[] rawBytes;

            using (MemoryStream memoryStream = stream as MemoryStream)
            {
                rawBytes = memoryStream.ToArray();
                using (FileStream fileStream = new FileStream(tempFileName, FileMode.Create))
                {
                    fileStream.Write(rawBytes, 0, rawBytes.Length);
                }
            }

            archiveIsTemporary = true;
            readArchiveEntries(tempFileName);
        }

        public void ExtractArchive(string directory) => 
            runSevenZip($"x -y -o\"{directory}\" \"{archiveFullName}\"");

        public void ExtractFile(string fileName, Stream stream)
        {
            Process process = runSevenZip($"x -so \"{archiveFullName}\" \"{fileName}\"");
            FileStream baseStream = process.StandardOutput.BaseStream as FileStream;

            int lastRead = 0;
            byte[] buffer = new byte[4096];

            do
            {
                lastRead = baseStream.Read(buffer, 0, buffer.Length);
                stream.Write(buffer, 0, lastRead);
            } while (lastRead > 0);
        }

        public void ExtractFile(int index, Stream stream) => 
            ExtractFile(archiveEntries[0].get("Path"), stream);

        public void Dispose() { if (archiveIsTemporary) File.Delete(archiveFullName); }

        void readArchiveEntries(string archiveName)
        {
            archiveFullName = archiveName;

            Process process = runSevenZip($"l -slt \"{archiveFullName}\"");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Exit code not zero: {process.ExitCode}\n{output}");
            }

            string[] outputParts = output.Split(new[] { "\n----------\n" }, StringSplitOptions.None);

            if (outputParts.Length != 2)
            {
                throw new Exception("Could not find output seperator!");
            }

            string header = outputParts[0];
            string body = outputParts[1];

            archiveEntries = body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(entry => new ArchiveEntry(entry)).ToList();
        }

        class ArchiveEntry
        {
            readonly Dictionary<string, string> entryData = new Dictionary<string, string>();

            public string get(string key) { return entryData[key]; }

            public ArchiveEntry(string rawEntry)
            {
                entryData = rawEntry.Split('\n')
                                    .Select(prop => prop.Split(new[] { " = " }, StringSplitOptions.None))
                                    .ToDictionary(keyValue => keyValue[0].Replace(" ", ""), keyValue => keyValue[1]);
            }
        }
    }
    #endregion

    #region Implementations for Windows

    class SevenZipWindows
    {
        static void SetLibraryPath()
        {
            string dllName = IntPtr.Size == 8 ? @"tools\7z64.dll" : @"tools\7z.dll";
            SevenZip.SevenZipBase.SetLibraryPath(Path.Combine(Program.BaseDirectoryInternal, dllName));
        }

        protected SevenZipWindows() { SetLibraryPath(); }
    }

    class SevenZipCompressorWindows : SevenZipWindows, ISevenZipCompressor
    {
        readonly SevenZip.SevenZipCompressor compressor = new SevenZip.SevenZipCompressor();

        public CompressionLevel CompressionLevel
        {
            get { return translateCompressionLevel(compressor.CompressionLevel); }
            set { compressor.CompressionLevel = translateCompressionLevel(value); }
        }

        public void CompressFiles(string archiveName, params string[] fileFullNames) =>
            compressor.CompressFiles(archiveName, fileFullNames);

        CompressionLevel translateCompressionLevel(SevenZip.CompressionLevel compressionLevel) => 
            (CompressionLevel) Enum.Parse(typeof(CompressionLevel), compressionLevel.ToString());

        SevenZip.CompressionLevel translateCompressionLevel(CompressionLevel compressionLevel) =>
            (SevenZip.CompressionLevel) Enum.Parse(typeof(SevenZip.CompressionLevel), compressionLevel.ToString());
    }

    class SevenZipExtractorWindows : SevenZipWindows, ISevenZipExtractor
    {
        readonly SevenZip.SevenZipExtractor extractor;
        public ReadOnlyCollection<string> ArchiveFileNames { get { return extractor.ArchiveFileNames; } }
        public SevenZipExtractorWindows(string fullName) { extractor = new SevenZip.SevenZipExtractor(fullName); }
        public SevenZipExtractorWindows(Stream stream) { extractor = new SevenZip.SevenZipExtractor(stream); }
        public void ExtractArchive(string directory) => extractor.ExtractArchive(directory);
        public void ExtractFile(string fileName, Stream stream) => extractor.ExtractFile(fileName, stream);
        public void ExtractFile(int index, Stream stream) => extractor.ExtractFile(index, stream);
        public void Dispose() => extractor.Dispose();
    }
    #endregion
}
