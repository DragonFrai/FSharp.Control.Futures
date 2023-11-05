namespace FSharp.Control.Futures

// Модуль содержит определения самых базовых типов
// с минимумом функционала, достаточно няпрямую следующими из их определения.
// Определение достаточно расплывчатое, но, к примеру, прямые вызовы функций сюда подходит
// А вот комбинаторы Future, даже самые фундаментальные, нет

open System

// [Core types]

/// <summary> Current state of a AsyncComputation </summary>
type [<Struct; RequireQualifiedAccess>]
    Poll<'a> =
    | Ready of result: 'a
    | Pending
    | Transit of transitTo: IFuture<'a>

/// # Ideal Future poll schema:
/// 1. Complete with result:
///   [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Ready x -> [ ! FutureTerminatedException ]
/// 2. Complete with transit
///   [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Transit f -> [ ! FutureTerminatedException ]
/// 3. Complete with exception (~ complete with result)
///   [ Poll.Pending -> ... -> Poll.Pending ] -> raise exn -> [ ! FutureTerminatedException ]
and IFuture<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    abstract Poll: context: IContext -> Poll<'a>

    /// <summary> Cancel Future and clean resources </summary>
    /// <remarks> It should always be called if the result of Future is no longer needed, and it is not yet terminal.
    /// It is a necessary requirement not to leave hanging futures and not to create conditions of eternal waiting.
    /// For example, merge should not leave a hanging Future if the second one throws an exception. </remarks>
    abstract Drop: unit -> unit

/// <summary> The context of the running computation.
/// Allows the computation to signal its ability to move forward (awake) through the Wake method </summary>
and IContext =
    /// <summary> Wake up assigned Future </summary>
    abstract Wake: unit -> unit

/// <summary> Scheduler Future. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread). </summary>
and IScheduler =
    inherit IDisposable

    abstract Spawn: IFuture<'a> -> IFutureTask<'a>

/// <summary> Allows to cancel and wait (asynchronously or synchronously) for a spawned Future. </summary>
and IFutureTask<'a> =
    abstract Cancel: unit -> unit
    abstract Await: unit -> IFuture<'a> // WaitAsync
    abstract WaitBlocking: unit -> 'a

// [Aliases]

type Future<'a> = IFuture<'a>

// [Exceptions]

/// Exception is thrown when future is in a terminated state:
/// Completed, Completed with exception, Canceled
type FutureTerminatedException internal () = inherit Exception()
type FutureDroppedException internal () = inherit FutureTerminatedException()
exception FutureThreadingException

exception FutureTaskAbortedException

[<AutoOpen>]
module Exceptions =
    let FutureTerminatedException : FutureTerminatedException = FutureTerminatedException()
    let FutureCancelledException : FutureDroppedException = FutureDroppedException()


// [Modules]
// [Modules / Poll]

[<RequireQualifiedAccess>]
module Poll =
    let inline isReady (poll: Poll<'a>) : bool =
        match poll with Poll.Ready _ -> true | _ -> false

    let inline isPending (poll: Poll<'a>) : bool =
        match poll with Poll.Pending -> true | _ -> false

    let inline isTransit (poll: Poll<'a>) : bool =
        match poll with Poll.Transit _ -> true | _ -> false

// [Modules / Future]
module Future =
    // Poll и Drop не являются первостепенными функциями пользовательского пространства,
    // поэтому не могут быть отражены в этом модуле.
    // Рассмотрите возможность использования Internals
    ()

// [Modules / Scheduler]
module Scheduler =
    let spawn (fut: Future<'a>) (scheduler: IScheduler) : IFutureTask<'a> = scheduler.Spawn(fut)

// [Modules / FutureTask]
module FutureTask =
    let await (futTask: IFutureTask<'a>) : Future<'a> = futTask.Await()
    let cancel (futTask: IFutureTask<'a>) : unit = futTask.Cancel()
