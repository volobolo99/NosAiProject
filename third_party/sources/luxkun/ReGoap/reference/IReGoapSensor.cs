// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/IReGoapSensor.cs
// UPSTREAM REVISION (blob SHA): 89f4a83685da23b687826d9aa9e79750dc6d1796
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

namespace ReGoap.Core
{
    public interface IReGoapSensor<T, W>
    {
        void Init(IReGoapMemory<T, W> memory);
        IReGoapMemory<T, W> GetMemory();
        void UpdateSensor();
    }
}
