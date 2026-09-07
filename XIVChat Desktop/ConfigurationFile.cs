using System;
using System.IO;
using System.Text;

namespace XIVChat_Desktop {
    // Kept independent of the UI so recovery and interrupted writes can be regression-tested.
    internal static class ConfigurationFile {
        internal static T? Load<T>(string path, Func<string, T> deserialize, out bool usedBackup) where T : class {
            usedBackup = false;
            Exception? primaryError = null;
            if (File.Exists(path)) {
                try {
                    return deserialize(File.ReadAllText(path));
                } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
                    primaryError = ex;
                }
            }

            if (File.Exists(path + ".bak")) {
                try {
                    var recovered = deserialize(File.ReadAllText(path + ".bak"));
                    usedBackup = true;
                    return recovered;
                } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
                    throw new IOException("Could not read the configuration or its backup.",
                        primaryError == null ? ex : new AggregateException(primaryError, ex));
                }
            }

            if (primaryError != null) throw new IOException("Could not read the configuration.", primaryError);
            return null;
        }

        internal static void Save(string path, string contents, Action<string> validate) {
            // Serialize and validate before touching the existing file.
            validate(contents);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    var bytes = new UTF8Encoding(false).GetBytes(contents);
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path)) {
                    var backupPath = path + ".bak";
                    try {
                        validate(File.ReadAllText(path));
                    } catch (InvalidDataException) {
                        // Never rotate a damaged primary over a known-good backup.
                        backupPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
                    }
                    File.Replace(tempPath, path, backupPath);
                } else {
                    File.Move(tempPath, path);
                }
            } finally {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
