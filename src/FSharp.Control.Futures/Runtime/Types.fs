namespace rec FSharp.Control.Futures.Runtime

open System
open FSharp.Control.Futures


// [ Exception ]

type FutureTaskAbortedException() = inherit Exception()
type FutureTaskMultipleAwaitException() = inherit Exception()

[<Struct>]
[<RequireQualifiedAccess>]
type AwaitMode =
    | Foreground // Abort on drop
    | Background // Not abort on drop

// type AwaitException =
//     inherit Exception
//     val error: AwaitError
//     new(error: AwaitError) = { inherit Exception($"{error}"); error = error }

[<Struct>]
[<RequireQualifiedAccess>]
type AwaitError =
    | Aborted
    | Failed of exn
    // with
    //     member this.IsAborted': bool = match this with AwaitError.Aborted -> true | _ -> false
    //     member this.IsFailed': bool = match this with AwaitError.Failed _ -> true | _ -> false

type AwaitResult<'a> = Result<'a, AwaitError>

/// <summary>
/// Safe wrapper for spawned Future. Allows to await and abort it. <br></br>
/// </summary>
type IFutureTask<'a> =
    /// <summary>
    /// Отменяет запущенную Future. В зависимости от реализации может как попросить планировщик вызвать <c>Drop</c> на
    /// фактической <c>Future</c> так и сделать это самостоятельно, блокируя вызывающий поток.
    /// </summary>
    ///
    /// <remarks>
    /// - Учитывайте, что реализации могут и вовсе не поддерживать Abort и игнорировать его.
    /// <br/>
    /// - Допустим множественный вызов.
    /// </remarks>
    abstract Abort: unit -> unit

    /// <summary>
    /// Получает Future, с помощью которой можно дождаться выполнения этой IFutureTask.
    /// Возвращенная Future будет прерывать выполнение связанной IFutureTask при своем Drop.
    /// (Аналогичен <c>this.Await(false)</c>)
    /// </summary>
    ///
    /// <remarks>
    /// Выбор по-умолчанию обусловлен тем,
    /// что в случае ошибок отсутствие работы обычно проще заметить, чем её игнорирование.
    /// </remarks>
    abstract Await: unit -> Future<AwaitResult<'a>>

    /// <summary>
    /// Получает <c>Future</c>, с помощью которой можно дождаться выполнения этой <c>IFutureTask</c>.
    /// Параметр <paramref name="background"/> определяет будет ли задача прекращаться при отбрасывании Future.
    /// </summary>
    ///
    /// <param name="background">
    /// Режим отбрасывания возвращаемой Future.
    /// При значении true, Drop не будет приводить к автоматическому вызову Abort запущенной задачи.
    /// При значение false, Drop будет приводить к автоматическому вызову Abort запущенной задачи.
    /// <br/>
    /// Вы все еще можете вызывать Abort самостоятельно, это лишь приведет к возврату Error AwaitError.Aborted
    /// ожидающей Future (при условии что рантайм поддерживает прекращение задач).
    /// </param>
    ///
    /// <remarks>
    /// Для каждого экземпляра IFutureTask Await можно вызвать ТОЛЬКО один раз,
    /// (т.к. сам по себе он не обязан предоставлять механизм синхронизации множественного ожидания).
    /// Но если вам нужно ожидание единственной <c>Future</c> в нескольких местах, можете
    /// рассмотреть возможность использования <see cref="FSharp.Control.Futures.Sync.OnceVar">OnceVar</see>.
    /// </remarks>
    abstract Await: background: bool -> Future<AwaitResult<'a>>

// type IAbortHandle =
//     abstract Abort: unit -> unit

/// <summary> Future Executor. Allows the Future to run for execution
/// (for example, on its own or shared thread pool or on the current thread). </summary>
type IRuntime =
    abstract Spawn: future: Future<'a> -> IFutureTask<'a>


// [Modules]

module Runtime =
    let inline spawn (executor: IRuntime) (fut: Future<'a>) : IFutureTask<'a> = executor.Spawn(fut)

module FutureTask =
    let inline await (futTask: IFutureTask<'a>) : Future<AwaitResult<'a>> = futTask.Await()
    let inline abort (futTask: IFutureTask<'a>) : unit = futTask.Abort()
