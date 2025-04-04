namespace LogStatTool.Helpers
{
    public class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Length != y.Length)
                return false;
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                    return false;
            }
            return true;
        }

        public int GetHashCode(byte[] obj)
        {
            if (obj is null)
                throw new ArgumentNullException(nameof(obj));

            // Simple hash combining method for demonstration.
            int hash = 17;
            foreach (var b in obj)
            {
                hash = hash * 31 + b;
            }
            return hash;
        }
    }
}