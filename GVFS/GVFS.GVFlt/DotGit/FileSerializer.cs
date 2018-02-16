using GVFS.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.GVFlt.DotGit
{
    public class FileSerializer
    {
        private readonly string filePath;
        private GVFSContext context;
        private Stream fileStream;
        private StreamWriter fileWriter;

        public FileSerializer(GVFSContext context, string filePath)
        {
            this.context = context;
            this.filePath = filePath;
        }

        public IEnumerable<string> ReadAll()
        {
            using (Stream stream = this.context.FileSystem.OpenFileStream(
                    this.filePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Read,
                    FileShare.None,
                    callFlushFileBuffers: false))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        yield return reader.ReadLine();
                    }
                }
            }
        }

        /// <summary>
        /// Appends the specified line to the file (using \n line breaks).
        /// AppendLine will open the file (if the file was not previously opened by a call to AppendLine).
        /// Callers must call Close() after calling AppendLine, as AppendLine leaves the file open.
        /// </summary>
        /// <param name="line">Line to append</param>
        public void AppendLine(string line)
        {
            this.Open();
            this.fileWriter.Write(line + "\n");
        }

        public void ReplaceFile(HashSet<string> lines)
        {
            this.Close();
            string tempFilePath = this.filePath + ".temp";
            this.context.FileSystem.WriteAllText(tempFilePath, string.Join("\n", lines) + "\n");
            this.context.FileSystem.MoveAndOverwriteFile(tempFilePath, this.filePath);
        }

        public void Close()
        {
            if (this.fileWriter != null)
            {
                this.fileWriter.Dispose();
                this.fileWriter = null;
            }

            if (this.fileStream != null)
            {
                this.fileStream.Dispose();
                this.fileStream = null;
            }
        }

        private void Open()
        {
            try
            {
                if (this.fileStream == null)
                {
                    this.fileStream = this.context.FileSystem.OpenFileStream(
                        this.filePath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        callFlushFileBuffers: false);
                    this.fileStream.Position = this.fileStream.Length;
                }

                if (this.fileWriter == null)
                {
                    this.fileWriter = new StreamWriter(this.fileStream);
                    this.fileWriter.AutoFlush = true;
                }
            }
            catch (Exception)
            {
                this.Close();
                throw;
            }
        }
    }
}
