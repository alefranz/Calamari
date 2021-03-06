using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Calamari.Integration.FileSystem;

namespace Calamari.Integration.Processes.Semaphores
{
    internal class LockIo : ILockIo
    {
        private readonly ICalamariFileSystem fileSystem;

        internal LockIo(ICalamariFileSystem fileSystem)
        {
            this.fileSystem = fileSystem;
        }

        public string GetFilePath(string lockName)
        {
            return Path.Combine(Path.GetTempPath(), lockName + ".lck");
        }

        public bool LockExists(string lockFilePath)
        {
            return fileSystem.FileExists(lockFilePath);
        }

        public FileLock ReadLock(string lockFilePath)
        {
            try
            {
                using (var stream = fileSystem.OpenFileExclusively(lockFilePath, FileMode.Open, FileAccess.Read))
                {
                    using (var streamReader = new StreamReader(stream))
                    {
                        var obj = JObject.Load(new JsonTextReader(streamReader));
                        var lockContent = new FileLock
                        {
                            ProcessId = obj["ProcessId"].ToObject<long>(),
                            ProcessName = obj["ProcessName"].ToString(),
                            Timestamp = obj["Timestamp"].ToObject<long>(),
                            ThreadId = obj["ThreadId"].ToObject<int>()
                        };
                        if (lockContent.BelongsToCurrentProcessAndThread())
                        {
                            return lockContent;
                        }
                        return new OtherProcessOwnsFileLock(lockContent);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return new MissingFileLock();
            }
            catch (IOException)
            {
                return new OtherProcessHasExclusiveLockOnFileLock();
            }
            catch (JsonReaderException)
            {
                return new UnableToDeserialiseLockFile(fileSystem.GetCreationTime(lockFilePath));
            }
            catch (Exception) //We have no idea what went wrong - reacquire this lock
            {
                return new OtherProcessHasExclusiveLockOnFileLock();
            }
        }

        public bool WriteLock(string lockFilePath, FileLock fileLock)
        {
            try
            {
                var fileMode = FileMode.CreateNew;
                if (LockExists(lockFilePath))
                {
                    var currentContent = ReadLock(lockFilePath);
                    if (Equals(currentContent, fileLock))
                    {
                        if (currentContent.Timestamp == fileLock.Timestamp)
                            return true;
                        fileMode = FileMode.Create;
                    }
                    else if (currentContent.GetType() == typeof(UnableToDeserialiseLockFile))
                    {
                        DeleteLock(lockFilePath);
                    }
                }

                var obj = new JObject
                {
                    ["ProcessId"] = fileLock.ProcessId,
                    ["ThreadId"] = fileLock.ThreadId,
                    ["ProcessName"] = fileLock.ProcessName,
                    ["Timestamp"] = fileLock.Timestamp
                };
                using (var stream = fileSystem.OpenFileExclusively(lockFilePath, fileMode, FileAccess.Write))
                {
                    using (var streamWriter = new StreamWriter(stream))
                    {
                        obj.WriteTo(new JsonTextWriter(streamWriter));
                    }
                }

                var writtenContent = ReadLock(lockFilePath);
                return Equals(writtenContent, fileLock);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void DeleteLock(string lockFilePath)
        {
            try
            {
                fileSystem.DeleteFile(lockFilePath);
            }
            catch (Exception)
            {
                // ignored - handled in create
            }
        }
    }
}