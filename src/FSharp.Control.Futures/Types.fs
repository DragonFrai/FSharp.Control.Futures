namespace FSharp.Control.Futures

open System


/// <summary> Current state of a AsyncComputation </summary>
type [<Struct; RequireQualifiedAccess>]
    Poll<'a> =
    | Ready of readyValue: 'a
    | Pending
    | Transit of transitComputation: IFuture<'a>

/// # IAsyncComputation poll schema
/// [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
and IFuture<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    //[<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll: context: IContext -> Poll<'a>

    /// <summary> Cancel asynchronously Computation computation </summary>
    /// <remarks> Notifies internal asynchronous operations of Computation cancellations. </remarks>
    //[<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

and Future<'a> = IFuture<'a>

/// <summary> The context of the running computation.
/// Allows the computation to signal its ability to move forward (awake) through the Wake method </summary>
and IContext =
    /// <summary> Wake up assigned Future </summary>
    abstract Wake: unit -> unit
    /// Current scheduler
    abstract Scheduler: IScheduler option

/// <summary> Scheduler Future. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread).  </summary>
and IScheduler =
    inherit IDisposable
    /// IScheduler.Spawn принимает Future, так как вызов Future.RunComputation является частью асинхронного вычисления.
    abstract Spawn: Future<'a> -> IJoinHandle<'a>

/// <summary> Allows to cancel and wait (asynchronously or synchronously) for a spawned Future. </summary>
and IJoinHandle<'a> =
    abstract Cancel: unit -> unit
    abstract Join: unit -> 'a
    abstract Await: unit -> Future<'a>
