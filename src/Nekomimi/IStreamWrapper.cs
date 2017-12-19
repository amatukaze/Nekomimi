using System;
using System.Threading.Tasks;

namespace Sakuno.Nekomimi
{
    internal interface IStreamWrapper
    {
        ValueTask<int> ReadAsync(ArraySegment<byte> buffer);
    }
}
