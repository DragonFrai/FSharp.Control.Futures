namespace FSharp.Control.Futures.Executing

open System
open FSharp.Control.Futures


// [Types]

/// <summary> Allows to cancel and wait (asynchronously or synchronously) for a spawned Future. </summary>
type IFutureTask<'a> =
    /// <summary>
    /// Отменяет запущенную Future. В зависимости от реализации может как попросить планировщик вызвать <c>Drop</c> на
    /// фактической <c>Future</c> так и сделать это самостоятельно, блокируя вызывающий поток.
    /// Двойной вызов невозможен.
    /// </summary>
    abstract Abort: unit -> unit

    /// <summary>
    /// Получает <c>Future</c>, с помощью которой можно дождаться выполнения этой <c>IFutureTask</c>.
    /// Полученная <c>Future</c> не будет отменять запущенную при своем <c>Drop</c>.
    /// Чтобы отменить выполнение работы, вызовите <c>Abort</c>.
    /// </summary>
    /// <remarks>
    /// Для каждого экземпляра <c>IFutureTask</c> <c>Await</c> можно вызвать ТОЛЬКО один раз,
    /// (т.к. сам по себе он не обязан предоставлять механизм синхронизации множественного ожидания).
    /// Но если вам нужно ожидание единственной <c>Future</c> в нескольких местах, можете
    /// рассмотреть возможность использования <see cref="FSharp.Control.Futures.Sync.OnceVar">OnceVar</see>
    /// </remarks>
    abstract Await: unit -> IFuture<'a>

/// <summary> Future Executor. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread). </summary>
type IExecutor =
    inherit IDisposable

    abstract Spawn: Future<'a> -> IFutureTask<'a>


// [ Exception ]

type FutureTaskAbortedException internal () = inherit Exception()

[<AutoOpen>]
module Exceptions =
    let FutureAbortedException : FutureTaskAbortedException = FutureTaskAbortedException()

// [Modules]

module Executor =
    let inline spawn (fut: Future<'a>) (executor: IExecutor) : IFutureTask<'a> = executor.Spawn(fut)

module FutureTask =
    let inline await (futTask: IFutureTask<'a>) : Future<'a> = futTask.Await()
    let inline abort (futTask: IFutureTask<'a>) : unit = futTask.Abort()
