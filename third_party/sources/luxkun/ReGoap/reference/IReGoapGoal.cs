// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/IReGoapGoal.cs
// UPSTREAM REVISION (blob SHA): 005de38fbdb603b27b419f2044f33bc1aaad3e27
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

using System;
using System.Collections.Generic;
using ReGoap.Planner;

namespace ReGoap.Core
{
    public interface IReGoapGoal<T, W>
    {
        void Run(Action<IReGoapGoal<T, W>> callback);
        Queue<ReGoapActionState<T, W>> GetPlan();
        string GetName();
        void Precalculations(IGoapPlanner<T, W> goapPlanner);
        bool IsGoalPossible();
        ReGoapState<T, W> GetGoalState();
        float GetPriority();
        void SetPlan(Queue<ReGoapActionState<T, W>> path);
        float GetErrorDelay();
    }
}
