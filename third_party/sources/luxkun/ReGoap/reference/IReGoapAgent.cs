// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/IReGoapAgent.cs
// UPSTREAM REVISION (blob SHA): 667203a1fda4bcf83093c6aa16436a3c7da04685
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

using System.Collections.Generic;

namespace ReGoap.Core
{
    public interface IReGoapAgent<T, W>
    {
        IReGoapMemory<T, W> GetMemory();
        IReGoapGoal<T, W> GetCurrentGoal();
        void WarnPossibleGoal(IReGoapGoal<T, W> goal);
        bool IsActive();
        List<ReGoapActionState<T, W>> GetStartingPlan();
        W GetPlanValue(T key);
        void SetPlanValue(T key, W value);
        bool HasPlanValue(T target);
        List<IReGoapGoal<T, W>> GetGoalsSet();
        List<IReGoapAction<T, W>> GetActionsSet();
        ReGoapState<T, W> InstantiateNewState();
    }
}
