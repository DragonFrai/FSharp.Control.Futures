namespace FSharp.Control.Futures.Runtime

open System
open FSharp.Control.Futures


// [ Exception ]

type FutureTaskAbortedException() = inherit Exception()
type FutureTaskMultipleAwaitException() = inherit Exception()

[<Struct>]
type AwaitMode =
    | Foreground // Abort on drop
    | Background // Not abort on drop

[<Struct>]
type AwaitError =
    | Aborted
    | Failed of exn
    // with
    //     member this.IsAborted: bool = match this with AwaitError.Aborted -> true | _ -> false
    //     member this.IsFailed: bool = match this with AwaitError.Failed _ -> true | _ -> false

type AwaitResult<'a> = Result<'a, AwaitError>

/// <summary>
/// Safe wrapper for spawned future. Allows to await and abort for a spawned Future. <br></br>
///
/// </summary>
type IFutureTask<'a> =
    /// <summary>
    /// Отменяет запущенную Future. В зависимости от реализации может как попросить планировщик вызвать <c>Drop</c> на
    /// фактической <c>Future</c> так и сделать это самостоятельно, блокируя вызывающий поток.
    /// Допустим множественный вызов.
    /// </summary>
    abstract Abort: unit -> unit

    /// <summary>
    /// Аналогичен <c>this.Await(false)</c>.
    /// </summary>
    abstract Await: unit -> Future<AwaitResult<'a>>

    /// <summary>
    /// Получает <c>Future</c>, с помощью которой можно дождаться выполнения этой <c>IFutureTask</c>.
    /// В зависимости от флага <c>background</c>,
    /// полученная <c>Future</c> (не) будет отменять задачу при своем <c>Drop</c>.
    /// Чтобы отменить выполнение задачи, в любой момент, вызовете <c>Abort</c>.
    /// То что ожидающая сторона не получит результат при этом не гарантируется.
    /// </summary>
    /// <remarks>
    /// Для каждого экземпляра <c>IFutureTask</c> <c>Await</c> можно вызвать ТОЛЬКО один раз,
    /// (т.к. сам по себе он не обязан предоставлять механизм синхронизации множественного ожидания).
    /// Но если вам нужно ожидание единственной <c>Future</c> в нескольких местах, можете
    /// рассмотреть возможность использования <see cref="FSharp.Control.Futures.Sync.OnceVar">OnceVar</see>.
    ///
    /// Также реализации имеют право ничего не делать при Abort.
    /// </remarks>
    abstract Await: background: bool -> Future<AwaitResult<'a>>

// type IAbortHandle =
//     abstract Abort: unit -> unit

/// <summary> Future Executor. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread). </summary>
type IRuntime =
    inherit IDisposable

    abstract Spawn: future: Future<'a> -> IFutureTask<'a>


// [Modules]

module Runtime =
    let inline spawn (executor: IRuntime) (fut: Future<'a>) : IFutureTask<'a> = executor.Spawn(fut)

module FutureTask =
    let inline await (futTask: IFutureTask<'a>) : Future<AwaitResult<'a>> = futTask.Await()
    let inline abort (futTask: IFutureTask<'a>) : unit = futTask.Abort()
