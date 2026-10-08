using System;
using System.Collections.Generic;

namespace Phinix.LegacyRedPacketExtension.Client
{
    // Value-only presentation state. It cannot own items or acknowledge protocol events.
    internal sealed class RedPacketSummaryQueue
    {
        internal sealed class Notice
        {
            internal string Key, Owner, Title, Text;
            internal bool Expired;
        }
        private const int Capacity = 64;
        private const int SeenCapacity = 512;
        private readonly Queue<Notice> pending = new Queue<Notice>();
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> seenOrder = new Queue<string>();
        internal int Count => pending.Count;
        internal bool Enqueue(Notice notice)
        {
            if (notice == null || string.IsNullOrEmpty(notice.Key) || string.IsNullOrEmpty(notice.Owner)) return false;
            if (seen.Contains(notice.Key)) return true;
            if (pending.Count >= Capacity) return false;
            // Snapshot strings only; never retain a packet, Thing, map or game service.
            var copy = new Notice { Key = notice.Key, Owner = notice.Owner, Expired = notice.Expired,
                Title = Limit(notice.Title, 256), Text = Limit(notice.Text, 64 * 1024) };
            seen.Add(copy.Key); seenOrder.Enqueue(copy.Key);
            while (seenOrder.Count > SeenCapacity) seen.Remove(seenOrder.Dequeue());
            pending.Enqueue(copy); return true;
        }
        internal void Drain(string owner, bool ready, Action<Notice> deliver, Action<Exception> failed)
        {
            int attempts = 0;
            while (pending.Count > 0 && attempts < 4)
            {
                Notice notice = pending.Peek();
                if (!string.Equals(owner, notice.Owner, StringComparison.Ordinal)) { pending.Dequeue(); attempts++; continue; }
                if (!ready) return;
                pending.Dequeue(); attempts++;
                // A letter hook can throw after adding the letter. Do not repeat that
                // uncertain side effect or retry an already committed item event.
                try { deliver(notice); }
                catch (Exception exception) { failed?.Invoke(exception); }
            }
        }
        internal void Clear() { pending.Clear(); seen.Clear(); seenOrder.Clear(); }
        private static string Limit(string value, int length)
        { return value == null ? string.Empty : value.Length <= length ? value : value.Substring(0, length - 1) + "…"; }
    }
}
