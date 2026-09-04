// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/IReGoapAction.cs
// UPSTREAM REVISION (blob SHA): cd0f12f20b30c61d00f49739187b0938bdbfdba6
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

using System;
using System.Collections.Generic;

namespace ReGoap.Core
{
    public struct GoapActionStackData<T, W>
    {
        public ReGoapState<T, W> currentState;
        public ReGoapState<T, W> goalState;
        public IReGoapAgent<T, W> agent;
        public IReGoapAction<T, W> next;
        public ReGoapState<T, W> settings;
    }

    public interface IReGoapAction<T, W>
    {
        List<ReGoapState<T, W>> GetSettings(GoapActionStackData<T, W> stackData);
        void Run(IReGoapAction<T, W> previousAction, IReGoapAction<T, W> nextAction, ReGoapState<T, W> settings, ReGoapState<T, W> goalState, Action<IReGoapAction<T, W>> done, Action<IReGoapAction<T, W>> fail);
        void PlanEnter(IReGoapAction<T, W> previousAction, IReGoapAction<T, W> nextAction, ReGoapState<T, W> settings, ReGoapState<T, W> goalState);
        void PlanExit(IReGoapAction<T, W> previousAction, IReGoapAction<T, W> nextAction, ReGoapState<T, W> settings, ReGoapState<T, W> goalState);
        void Exit(IReGoapAction<T, W> nextAction);
        string GetName();
        bool IsActive();
        bool IsInterruptable();
        void AskForInterruption();
        ReGoapState<T, W> GetPreconditions(GoapActionStackData<T, W> stackData);
        ReGoapState<T, W> GetEffects(GoapActionStackData<T, W> stackData);
        bool CheckProceduralCondition(GoapActionStackData<T, W> stackData);
        float GetCost(GoapActionStackData<T, W> stackData);
        void Precalculations(GoapActionStackData<T, W> stackData);
        string ToString(GoapActionStackData<T, W> stackData);
    }
}
