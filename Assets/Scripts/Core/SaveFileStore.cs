using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Tactics.Core
{
    public sealed class SaveFileStore
    {
        private readonly string directory;
        public SaveFileStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A save directory is required.");
            this.directory = Path.GetFullPath(directory);
        }
        public static bool IsValidName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 64
                && !name.Equals("saveManager", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(name, @"\A[a-zA-Z0-9]+\z");
        }
        private string SavePath(string name)
        {
            if (!IsValidName(name)) throw new ArgumentException("Use 1–64 letters or digits; saveManager is reserved.");
            return Path.Combine(directory, name + ".json");
        }
        public bool Exists(string name) => File.Exists(SavePath(name));
        public string Read(string name) => File.ReadAllText(SavePath(name));
        public void Write(string name, string contents) => AtomicWrite(SavePath(name), contents);
        public string ReadIndex()
        {
            var file = Path.Combine(directory, "saveManager.json");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        public void WriteIndex(string contents) => AtomicWrite(Path.Combine(directory, "saveManager.json"), contents);
        public void Delete(string name)
        {
            string file = SavePath(name);
            File.Delete(file);
            File.Delete(file + ".bak");
        }
        public string[] ListNames()
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            var names = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (IsValidName(name)) names.Add(name);
            }
            return names.ToArray();
        }
        private void AtomicWrite(string file, string contents)
        {
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            Directory.CreateDirectory(directory);
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backup = file + ".bak";
            try
            {
                File.WriteAllText(temporary, contents);
                if (!File.Exists(file)) File.Move(temporary, file);
                else
                {
                    try { File.Replace(temporary, file, backup); }
                    catch (PlatformNotSupportedException)
                    {
                        // Rename with rollback on platforms without File.Replace.
                        File.Delete(backup);
                        File.Move(file, backup);
                        try { File.Move(temporary, file); }
                        catch { File.Move(backup, file); throw; }
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
