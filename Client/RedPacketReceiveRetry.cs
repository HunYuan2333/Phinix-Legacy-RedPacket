using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Utils.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    [DataContract]
    internal sealed class RedPacketReceiveAttempt
    {
        [DataMember] public string Message { get; set; }
        [DataMember] public string Recipient { get; set; }
        [DataMember] public int Attempts { get; set; }
        [DataMember] public DateTime NextUtc { get; set; }
        [DataMember] public bool Paused { get; set; }
        [DataMember] public bool Uncertain { get; set; }
        [DataMember] public string Reason { get; set; }
    }

    // One file per stable message/recipient. Never advances or removes relay data.
    internal sealed class RedPacketReceiveRetry
    {
        private readonly Func<string, string> path;
        internal RedPacketReceiveAttempt Current { get; private set; }
        private string currentPath;
        internal RedPacketReceiveRetry(Func<string, string> path) { this.path = path; }
        internal bool Begin(string message, string recipient, DateTime now, bool safeToReplay = false)
        {
            if (Current == null || Current.Message != message || Current.Recipient != recipient)
            {
                string id;
                using (var sha = SHA256.Create())
                    id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(recipient.Length + ":" + recipient + message))).Replace("-", "");
                currentPath = path("receive-retry-" + id + ".json");
                Current = new RedPacketReceiveAttempt { Message = message, Recipient = recipient };
                try
                {
                    if (File.Exists(currentPath))
                    {
                        if (new FileInfo(currentPath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Receive recovery record exceeds its budget.");
                        var restored = FrameworkSerialization.DeserializePayload<RedPacketReceiveAttempt>(File.ReadAllText(currentPath));
                        if (restored == null || restored.Message != message || restored.Recipient != recipient || restored.Attempts < 0 || restored.Attempts > 5)
                            throw new InvalidDataException("Receive recovery record is invalid.");
                        Current = restored;
                        if (Current.Uncertain) Current.Paused = true;
                    }
                }
                catch { Current.Paused = true; Current.Uncertain = true; throw; }
            }
            if (Current.Paused || now < Current.NextUtc) return false;
            Current.Uncertain = !safeToReplay;
            Current.Attempts++;
            Current.NextUtc = now.AddSeconds(Math.Min(60, 2 << Math.Min(Current.Attempts, 5)));
            Current.Paused = Current.Attempts >= 5;
            Save(); // Reserve the attempt durably before any business side effect.
            return true;
        }
        internal void Fail(string reason, bool permanent, bool uncertain)
        {
            Current.Reason = reason;
            Current.Paused |= permanent || uncertain;
            Current.Uncertain = uncertain;
            Save();
        }
        internal void Retry()
        {
            if (Current == null || !Current.Paused || Current.Uncertain) return;
            Current.Attempts = 0; Current.Paused = false; Current.NextUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Save();
        }
        internal void Complete()
        {
            File.Delete(currentPath);
            Current = null;
        }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(currentPath));
                byte[] bytes = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(Current));
                if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Receive recovery record exceeds its budget.");
                int count = 0;
                long storedBytes = bytes.Length;
                foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(currentPath), "receive-retry-*.json"))
                {
                    if (file != currentPath) storedBytes += new FileInfo(file).Length;
                    if (++count > 512 || storedBytes > 128L * 1024 * 1024) throw new IOException("Receive recovery storage is full.");
                }
                using (var file = new FileStream(currentPath + ".new", FileMode.Create, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(currentPath)) File.Replace(currentPath + ".new", currentPath, null);
                else File.Move(currentPath + ".new", currentPath);
            }
            catch { Current.Paused = true; Current.Uncertain = true; throw; }
        }
    }
}
