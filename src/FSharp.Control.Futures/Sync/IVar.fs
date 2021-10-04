namespace FSharp.Control.Futures.Sync

open System.Collections.Generic
open FSharp.Control.Futures.Core
open FSharp.Control.Futures


[<Struct; RequireQualifiedAccess>]
type IVarWriteError =
    | DoubleWrite

exception IVarDoubleWriteErrorException


// TODO?: Add Cancelled variant?
[<Struct; RequireQualifiedAccess>]
type internal Value<'a> =
    | Blank
    | Written of value: 'a
    // Not a substitute for sending Option<'a>.
    | WrittenException of exn: exn

module internal Value =
    let inline internal asPollResult (value: Value<'a>) =
        match value with
        | Value.Blank -> Poll.Pending
        | Value.Written x -> Ok x |> Poll.Ready
        | Value.WrittenException e -> Error e |> Poll.Ready

/// An immutable cell to asynchronously wait for a single value.
/// Represents the pending future in which value can be put.
/// If you never put a value, you will endlessly wait for it.
/// Provides a mechanism for dispatching exceptions. It can be expressed through Result and is not recommended.
/// Of course, unless you cannot live without exceptions or you need to express a really critical case,
/// which is too expensive to wrap in "(x: IVar<Result<_, _>>) |> Future.raise".
[<Sealed>]
type IVar<'a>() =
    let syncObj = obj()
    let mutable _value = Value<'a>.Blank
    let mutable _waiters: LinkedList<IVarComputation<'a>> = nullObj

    member internal _.SyncObj = syncObj
    member internal _.Value = _value
    member internal _.Waiters = _waiters

    member internal _.WaitNoSync(comp: IVarComputation<'a>) =
        if isNull _waiters then
            _waiters <- LinkedList()
        _waiters.AddLast(comp)

    member internal _.CancelWaitNoSync(node: LinkedListNode<IVarComputation<'a>>) =
        _waiters.Remove(node)

    // return Error if put of the value failed (IVarDoublePutException)
    member inline private _.TryWriteInnerNoSync(x: Result<'a, exn>) : Result<unit, IVarWriteError> =
        match _value with
        | Value.Blank ->
            _value <- match x with Ok x -> Value.Written x | Error ex -> Value.WrittenException ex
            if isNotNull _waiters then
                for waiter in _waiters do waiter.WakeNoSync()
                _waiters <- nullObj
            Ok ()
        | Value.Written _ | Value.WrittenException _ ->
            Error IVarWriteError.DoubleWrite

    member this.TryWrite(x: 'a) =
        lock syncObj <| fun () ->
            this.TryWriteInnerNoSync(Ok x)

    member this.Write(x: 'a) =
        lock syncObj <| fun () -> this.TryWriteInnerNoSync(Ok x)
        |> function Error (_: IVarWriteError) -> raise IVarDoubleWriteErrorException | _ -> ()

    member this.TryWriteException(ex: exn) =
        lock syncObj <| fun () ->
            this.TryWriteInnerNoSync(Error ex)

    member this.WriteException(ex: exn) =
        lock syncObj <| fun () -> this.TryWriteInnerNoSync(Error ex)
        |> function Error (_: IVarWriteError) -> raise IVarDoubleWriteErrorException | _ -> ()

    member this.TryRead() =
        lock syncObj <| fun () ->
            match _value with
            | Value.Blank -> None
            | Value.Written x -> Some x
            | Value.WrittenException e -> raise e

    member inline this.Read() : Future<'a> = upcast this

    interface Future<'a> with
        member this.RunComputation() =
            upcast IVarComputation(this)

// IAsyncComputation version of IVar
and [<Sealed>] internal IVarComputation<'a>(ivar: IVar<'a>) =
    // selfNode notNull when _context notNull
    // Current waiter context
    let mutable _context: IContext = nullObj
    // Node for cancelling
    let mutable _selfNode: LinkedListNode<IVarComputation<'a>> = nullObj

    member internal _.WakeNoSync() =
        _context.Wake()
        ivar.CancelWaitNoSync(_selfNode)
        _context <- nullObj
        _selfNode <- nullObj

    interface IAsyncComputation<'a> with
        member this.Poll(ctx) =
            lock ivar.SyncObj <| fun () ->
                match ivar.Value with
                | Value.Written x ->
                    if isNotNull _context then
                        ivar.CancelWaitNoSync(_selfNode)
                        _selfNode <- nullObj
                        _context <- nullObj
                    Poll.Ready x
                | Value.Blank ->
                    _selfNode <- ivar.WaitNoSync(this)
                    _context <- ctx
                    Poll.Pending
                | Value.WrittenException e ->
                    _context <- nullObj
                    _selfNode <- nullObj
                    raise e

        member this.Cancel() =
            if isNotNull _context then
                ivar.CancelWaitNoSync(_selfNode)
                _context <- nullObj


module IVar =
    /// Create empty IVar instance
    let inline create () = IVar()

    /// Put a value and if it is already set raise exception
    let inline write (x: 'a) (ivar: IVar<'a>) = ivar.Write(x)

    /// Tries to put a value and if it is already set returns an Error
    let inline tryWrite (x: 'a) (ivar: IVar<'a>) = ivar.TryWrite(x)

    let inline writeException (ex: exn) (ivar: IVar<'a>) = ivar.WriteException(ex)

    let inline tryWriteException (ex: exn) (ivar: IVar<'a>) = ivar.TryWriteException(ex)

    /// Create future that write result of future in target.
    /// When future throw exception catch it and write in target.
    /// Throw exception when duplicate write in IVar
    let inline writeFrom (source: Future<'a>) (target: IVar<'a>) =
        future {
            try
                let! x = source
                target.Write(x)
            with e ->
                target.WriteException(e)
        }

    /// <summary> Returns the future pending value. </summary>
    /// <remarks> IVar itself is a future, therefore
    /// it is impossible to expect or launch this future in two places at once. </remarks>
    let inline read (ivar: IVar<'a>) = ivar.Read()

    /// Immediately gets the current IVar value and returns Some x if set
    let inline tryRead (ivar: IVar<'a>) = ivar.TryRead()

