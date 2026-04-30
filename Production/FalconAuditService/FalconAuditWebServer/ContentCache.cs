namespace FalconAuditService;

using System.Collections.Generic;

/// <summary>
/// Thread-safe LRU cache for P1 file content. Evicts oldest entries when the
/// total byte estimate exceeds MaxBytes. Each char is counted as 2 bytes.
/// </summary>
public class ContentCache
{
    private readonly long _maxBytes;
    private long _totalBytes;
    private readonly Dictionary<string, LinkedListNode<(string key, string value)>> _map =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string key, string value)> _order = new();
    private readonly object _lock = new();

    public ContentCache(long maxBytes = 200L * 1024 * 1024)   // 200 MB default
    {
        _maxBytes = maxBytes;
    }

    public void Set(string path, string content)
    {
        long newBytes = content.Length * 2L;
        lock (_lock)
        {
            if (_map.TryGetValue(path, out var existing))
            {
                _totalBytes -= existing.Value.value.Length * 2L;
                _order.Remove(existing);
                _map.Remove(path);
            }

            while (_totalBytes + newBytes > _maxBytes && _order.Count > 0)
            {
                var oldest = _order.First!;
                _totalBytes -= oldest.Value.value.Length * 2L;
                _map.Remove(oldest.Value.key);
                _order.RemoveFirst();
            }

            var node = _order.AddLast((path, content));
            _map[path] = node;
            _totalBytes += newBytes;
        }
    }

    public string? Get(string path)
    {
        lock (_lock)
            return _map.TryGetValue(path, out var node) ? node.Value.value : null;
    }

    public void Remove(string path)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(path, out var node)) return;
            _totalBytes -= node.Value.value.Length * 2L;
            _order.Remove(node);
            _map.Remove(path);
        }
    }
}
