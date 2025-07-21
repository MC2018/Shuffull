using System;

namespace Shuffull.Shared.Tools
{
    public static class IdGenerator
    {
        public static string Generate()
        {
            return Ulid.NewUlid().ToString().ToLowerInvariant();
        }
    }
}