using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Burger.Core.Modding;

/// <summary>
/// Step meter for sandboxed mod code. The mod compiler inserts a call to <see cref="Tick"/> at the start of every
/// function body (methods, accessors, lambdas, local functions), every loop iteration, every catch and finally block
/// and after every label, so a mod cannot opt out. Inside <see cref="Run{T}"/> a budget of steps and allocated bytes
/// applies; once it is used up every further step throws, including the first step of any catch block, so the mod
/// cannot swallow the stop. Steps are counted, not timed: the same input stops at the same point on every machine,
/// which keeps lockstep in sync when a mod's <c>apply</c> is cut off.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Fuel
{
    [ThreadStatic] private static bool t_on;
    [ThreadStatic] private static bool t_out;
    [ThreadStatic] private static long t_left;
    [ThreadStatic] private static long t_allocLimit;
    [ThreadStatic] private static string? t_why;

    /// <summary>One step of mod code. Called by compiled mods only; calling it more often only uses up the mod's own budget.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Tick()
    {
        // deep recursion would end the process (a stack overflow cannot be caught): stop it first, budget or not
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) throw new ModBudgetException("recursion too deep");
        if (!t_on) return;
        if (t_out || --t_left < 0) Out("ran out of steps");
        if ((t_left & 1023) == 0 && GC.GetAllocatedBytesForCurrentThread() > t_allocLimit) Out("allocated too much memory");
    }

    private static void Out(string why)
    {
        if (!t_out) { t_out = true; t_why = why; }
        throw new ModBudgetException(t_why!);
    }

    /// <summary>Runs mod code with a budget. Throws <see cref="ModBudgetException"/> when the budget is used up.</summary>
    internal static T Run<T>(long steps, long allocBytes, Func<T> body)
    {
        bool on = t_on, outPrev = t_out; long left = t_left, alloc = t_allocLimit; string? why = t_why;
        t_on = true; t_out = false; t_why = null;
        t_left = on ? Math.Min(left, steps) : steps; // a nested budget never extends the outer one
        long limit = GC.GetAllocatedBytesForCurrentThread() + allocBytes;
        t_allocLimit = on ? Math.Min(alloc, limit) : limit;
        try
        {
            var result = body();
            if (t_out) throw new ModBudgetException(t_why!); // the mod caught the stop somewhere it could not be re-thrown
            return result;
        }
        finally
        {
            long used = (on ? Math.Min(left, steps) : steps) - t_left;
            t_on = on; t_out = outPrev; t_why = why; t_allocLimit = alloc;
            t_left = on ? left - Math.Max(0, used) : left;
        }
    }

    /// <summary>True while mod code on this thread runs under a budget.</summary>
    internal static bool Metered => t_on;
}

/// <summary>Thrown inside mod code that used up its step or memory budget, or recursed too deep.</summary>
public sealed class ModBudgetException : Exception
{
    internal ModBudgetException(string message) : base(message) { }
}
