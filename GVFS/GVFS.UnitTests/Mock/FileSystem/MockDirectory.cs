using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.IO;

namespace GVFS.UnitTests.Mock.FileSystem
{
    public class MockDirectory
    {
        public MockDirectory(string fullName, IEnumerable<MockDirectory> folders, IEnumerable<MockFile> files)
        {
            this.FullName = fullName;
            this.Name = Path.GetFileName(this.FullName);

            this.Directories = new Dictionary<string, MockDirectory>(StringComparer.InvariantCultureIgnoreCase);
            this.Files = new Dictionary<string, MockFile>(StringComparer.InvariantCultureIgnoreCase);

            if (folders != null)
            {
                foreach (MockDirectory folder in folders)
                {
                    this.Directories[folder.FullName] = folder;
                }
            }

            if (files != null)
            {
                foreach (MockFile file in files)
                {
                    this.Files[file.FullName] = file;
                }
            }

            this.FileProperties = FileProperties.DefaultDirectory;
        }

        public string FullName { get; private set; }
        public string Name { get; private set; }
        public Dictionary<string, MockDirectory> Directories { get; private set; }
        public Dictionary<string, MockFile> Files { get; private set; }
        public FileProperties FileProperties { get; set; }

        public MockFile FindFile(string path)
        {
            MockFile file;
            if (this.Files.TryGetValue(path, out file))
            {
                return file;
            }

            foreach (MockDirectory directory in this.Directories.Values)
            {
                file = directory.FindFile(path);
                if (file != null)
                {
                    return file;
                }
            }

            return null;
        }

        public void AddOrOverwriteFile(MockFile file, string path)
        {
            string parentPath = path.Substring(0, path.LastIndexOf(Path.DirectorySeparatorChar));
            MockDirectory parentDirectory = this.FindDirectory(parentPath);

            if (parentDirectory == null)
            {
                throw new IOException();
            }

            MockFile existingFileAtPath = parentDirectory.FindFile(path);

            if (existingFileAtPath != null)
            {
                parentDirectory.Files.Remove(path);
            }

            parentDirectory.Files.Add(file.FullName, file);
        }

        public void AddFile(MockFile file, string path)
        {
            string parentPath = path.Substring(0, path.LastIndexOf(Path.DirectorySeparatorChar));
            MockDirectory parentDirectory = this.FindDirectory(parentPath);

            if (parentDirectory == null)
            {
                throw new IOException();
            }

            MockFile existingFileAtPath = parentDirectory.FindFile(path);
            existingFileAtPath.ShouldBeNull();

            parentDirectory.Files.Add(file.FullName, file);
        }

        public void RemoveFile(string path)
        {
            MockFile file;
            if (this.Files.TryGetValue(path, out file))
            {
                this.Files.Remove(path);
                return;
            }

            foreach (MockDirectory directory in this.Directories.Values)
            {
                file = directory.FindFile(path);
                if (file != null)
                {
                    directory.RemoveFile(path);
                    return;
                }
            }
        }

        public MockDirectory FindDirectory(string path)
        {
            if (path.Equals(this.FullName, StringComparison.InvariantCultureIgnoreCase))
            {
                return this;
            }

            MockDirectory foundDirectory;
            if (this.Directories.TryGetValue(path, out foundDirectory))
            {
                return foundDirectory;
            }

            foreach (MockDirectory subDirectory in this.Directories.Values)
            {
                foundDirectory = subDirectory.FindDirectory(path);
                if (foundDirectory != null)
                {
                    return foundDirectory;
                }
            }

            return null;
        }

        public MockFile CreateFile(string path)
        {
            return this.CreateFile(path, string.Empty);
        }

        public MockFile CreateFile(string path, string contents, bool createDirectories = false)
        {
            string parentPath = path.Substring(0, path.LastIndexOf(Path.DirectorySeparatorChar));
            MockDirectory parentDirectory = this.FindDirectory(parentPath);
            if (createDirectories)
            {
                if (parentDirectory == null)
                {
                    parentDirectory = this.CreateDirectory(parentPath);
                }
            }
            else
            {
                parentDirectory.ShouldNotBeNull();
            }

            MockFile newFile = new MockFile(path, contents);
            parentDirectory.Files.Add(newFile.FullName, newFile);

            return newFile;
        }

        public MockDirectory CreateDirectory(string path)
        {
            int lastSlashIdx = path.LastIndexOf(Path.DirectorySeparatorChar);

            if (lastSlashIdx <= 0)
            {
                return this;
            }

            string parentPath = path.Substring(0, lastSlashIdx);
            MockDirectory parentDirectory = this.FindDirectory(parentPath);
            if (parentDirectory == null)
            {
                parentDirectory = this.CreateDirectory(parentPath);
            }

            MockDirectory newDirectory;
            if (!parentDirectory.Directories.TryGetValue(path, out newDirectory))
            {
                newDirectory = new MockDirectory(path, null, null);
                parentDirectory.Directories.Add(newDirectory.FullName, newDirectory);
            }

            return newDirectory;
        }

        public void DeleteDirectory(string path)
        {
            if (path.Equals(this.FullName, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotSupportedException();
            }

            MockDirectory foundDirectory;
            if (this.Directories.TryGetValue(path, out foundDirectory))
            {
                this.Directories.Remove(path);
            }
            else
            {
                foreach (MockDirectory subDirectory in this.Directories.Values)
                {
                    foundDirectory = subDirectory.FindDirectory(path);
                    if (foundDirectory != null)
                    {
                        subDirectory.DeleteDirectory(path);
                        return;
                    }
                }
            }
        }

        public void MoveDirectory(string sourcePath, string targetPath)
        {
            MockDirectory sourceDirectory;
            MockDirectory sourceDirectoryParent;
            this.TryGetDirectoryAndParent(sourcePath, out sourceDirectory, out sourceDirectoryParent).ShouldEqual(true);

            int endPathIndex = targetPath.LastIndexOf(Path.DirectorySeparatorChar);
            string targetDirectoryPath = targetPath.Substring(0, endPathIndex);

            MockDirectory targetDirectory = this.FindDirectory(targetDirectoryPath);
            targetDirectory.ShouldNotBeNull();

            sourceDirectoryParent.RemoveDirectory(sourceDirectory);

            sourceDirectory.FullName = targetPath;

            targetDirectory.AddDirectory(sourceDirectory);
        }

        public void RemoveDirectory(MockDirectory directory)
        {
            this.Directories.ContainsKey(directory.FullName).ShouldEqual(true);
            this.Directories.Remove(directory.FullName);
        }

        private void AddDirectory(MockDirectory directory)
        {
            if (this.Directories.ContainsKey(directory.FullName))
            {
                MockDirectory oldDirectory = this.Directories[directory.FullName];
                foreach (MockFile newFile in directory.Files.Values)
                {
                    newFile.FullName = Path.Combine(oldDirectory.FullName, newFile.Name);
                    oldDirectory.AddOrOverwriteFile(newFile, newFile.FullName);
                }

                foreach (MockDirectory newDirectory in directory.Directories.Values)
                {
                    newDirectory.FullName = Path.Combine(oldDirectory.FullName, newDirectory.Name);
                    this.AddDirectory(newDirectory);
                }
            }
            else
            {
                this.Directories.Add(directory.FullName, directory);
            }
        }

        private bool TryGetDirectoryAndParent(string path, out MockDirectory directory, out MockDirectory parentDirectory)
        {
            if (this.Directories.TryGetValue(path, out directory))
            {
                parentDirectory = this;
                return true;
            }
            else
            {
                string parentPath = path.Substring(0, path.LastIndexOf(Path.DirectorySeparatorChar));
                parentDirectory = this.FindDirectory(parentPath);
                if (parentDirectory != null)
                {
                    foreach (MockDirectory subDirectory in this.Directories.Values)
                    {
                        directory = subDirectory.FindDirectory(path);
                        if (directory != null)
                        {
                            return true;
                        }
                    }
                }
            }

            directory = null;
            parentDirectory = null;
            return false;
        }
    }
}
