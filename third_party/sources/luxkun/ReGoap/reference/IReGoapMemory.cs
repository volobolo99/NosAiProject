// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/IReGoapMemory.cs
// UPSTREAM REVISION (blob SHA): 931ccf486fdf60248c9788ed5ead870c38c3be92
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

namespace ReGoap.Core
{
    public interface IReGoapMemory<T, W>
    {
        ReGoapState<T, W> GetWorldState();
    }
}
