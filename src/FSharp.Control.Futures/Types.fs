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
    | Ready of value: 'a
    | Pending
    | Transit of transitTo: IFuture<'a>

/// <summary>
/// Poll based async primitive with single return value.
///
/// Ideal Future poll schema:
/// 1. Complete with result:
///   [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Ready x -> [ ! FutureTerminatedException ]
/// 2. Complete with transit
///   [ Poll.Pending -> ... -> Poll.Pending ] -> Poll.Transit f -> [ ! FutureTerminatedException ]
/// 3. Complete with exception (~ complete with result)
///   [ Poll.Pending -> ... -> Poll.Pending ] -> raise exn -> [ ! FutureTerminatedException ]
/// </summary>
/// <remarks>
/// The Future is not required to throw exception in terminated state.
/// Polling or Dropping Future in terminated state - Undefined Behaviour.
/// </remarks>
and [<Interface>]
    IFuture<'a> =

    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    abstract Poll: context: IContext -> Poll<'a>

    /// <summary> Cancel Future and clean resources </summary>
    /// <remarks> It should always be called if the result of Future is no longer needed, and it is not yet terminal.
    /// It is a necessary requirement not to leave hanging futures and not to create conditions of eternal waiting.
    /// For example, merge should not leave a hanging Future if the second one throws an exception. </remarks>
    abstract Drop: unit -> unit

and Future<'a> = IFuture<'a>

and [<Interface>]
    IWaker =

    /// <summary> Wake up assigned Future </summary>
    abstract Wake: unit -> unit

/// <summary>
/// The context of the running Future.
/// Allows the Future to signal its ability to move forward (awake) through the Wake method
/// </summary>
and IContext =
    inherit IWaker

    /// <summary> Returns future context feature provider </summary>
    abstract Features: unit -> IFeatureProvider

/// <summary>
/// Provides a features that implemented by Future runner (runtime or poll loop or other).
/// Useful for get more efficient implementation of time, I/O, or other functions
/// or special actions (for example sleep in simulated time).
/// </summary>
and IFeatureProvider =
    abstract GetFeature<'a> : unit -> ValueOption<'a>

// [Exceptions]

/// Exception is thrown when future is in a terminated state:
/// Ready or Polled with exception
type FutureTerminatedException =
    inherit Exception
    new() = { inherit Exception() }
    new(message: string) = { inherit Exception(message) }

// [Modules]

[<RequireQualifiedAccess>]
module Poll =
    let inline isReady (poll: Poll<'a>) : bool =
        match poll with Poll.Ready _ -> true | _ -> false

    let inline isPending (poll: Poll<'a>) : bool =
        match poll with Poll.Pending -> true | _ -> false

    let inline isTransit (poll: Poll<'a>) : bool =
        match poll with Poll.Transit _ -> true | _ -> false
