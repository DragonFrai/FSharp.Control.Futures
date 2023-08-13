namespace FSharp.Control.Futures.Sync

open System.Threading
open FSharp.Control.Futures.Types
open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


exception IVarDoubleWriteException
exception IVarValueNotWrittenException

[<Struct; RequireQualifiedAccess>]
type internal State =
    | Blank
    | Written
    | WrittenFailure

/// An immutable cell to asynchronously wait for a single value.
/// Represents the pending future in which value can be put.
/// If you never put a value, you will endlessly wait for it.
/// Provides a mechanism for dispatching exceptions. It can be expressed through Result and is not recommended.
/// Of course, unless you cannot live without exceptions or you need to express a really critical case,
/// which is too expensive to wrap in "(x: IVar<Result<_, _>>) |> Future.raise".
[<Sealed>]
type IVar<'a>() =
    let _spinLock = SpinLock(false)
    let mutable _state = State.Blank
    let mutable _value: 'a = Unchecked.defaultof<_>
    let mutable _exception: exn = nullObj
    let mutable _waiters: IntrusiveList<IVarReadFuture<'a>> = IntrusiveList.Create()

    member inline internal this.RemoveWaiterNoSync(waiter: IVarReadFuture<'a>) =
        if isNotNull waiter.Context then
            waiter.Context <- nullObj
            _waiters.Remove(waiter) |> ignore

    member inline internal _.RegisterWaiterNoSync(waiter: IVarReadFuture<'a>, ctx: IContext) =
        waiter.Context <- ctx
        _waiters.PushBack(waiter)

    member inline internal this.PollResult(reader: IVarReadFuture<'a>, ctx: IContext) =
        // TODO: Add a non-blocking branch if a pending branch has not yet been added and a result exists
        let mutable hasLock = false
        _spinLock.Enter(&hasLock)
        match _state with
        | State.Written ->
            this.RemoveWaiterNoSync(reader)
            if hasLock then _spinLock.Exit()
            Poll.Ready _value
        | State.WrittenFailure ->
            this.RemoveWaiterNoSync(reader)
            if hasLock then _spinLock.Exit()
            raise _exception
        | State.Blank ->
            this.RegisterWaiterNoSync(reader, ctx)
            if hasLock then _spinLock.Exit()
            Poll.Pending

    member inline internal _.CancelRead(reader: IVarReadFuture<'a>) =
        if isNotNull reader.Context then
            let mutable hasLock = false
            _spinLock.Enter(&hasLock)
            reader.Context <- nullObj
            _waiters.Remove(reader) |> ignore
            if hasLock then _spinLock.Exit()

    member inline private _.WriteInner(x: 'a, ex: exn): unit =
        let mutable hasLock = false
        _spinLock.Enter(&hasLock)
        match _state with
        | State.Blank ->
            if isNull ex
            then
                _value <- x
                _state <- State.Written
            else
                _exception <- ex
                _state <- State.WrittenFailure

            let root = _waiters.Drain()
            if hasLock then _spinLock.Exit()

            let rec wakeLoop (fut: IVarReadFuture<'a>) =
                if isNotNull fut then
                    let ctx: IContext = fut.Context
                    fut.Context <- nullObj
                    ctx.Wake()
                    wakeLoop fut.next
            wakeLoop root

        | State.Written | State.WrittenFailure ->
            if hasLock then _spinLock.Exit()
            raise IVarDoubleWriteException

    member this.WriteValue(x: 'a) =
        this.WriteInner(x, nullObj)

    member this.WriteException(ex: exn) =
        this.WriteInner(Unchecked.defaultof<'a>, ex)

    member this.Write(fut: Future<'a>): Future<unit> = future {
        try
            let! x = fut
            this.WriteValue(x)
        with e ->
            this.WriteException(e)
    }

    member this.Read() : Future<'a> =
        upcast IVarReadFuture(this)

    member _.HasValue(): bool = _state <> State.Blank

    member this.Get(): 'a =
        match _state with
        | State.Blank -> raise IVarValueNotWrittenException
        | State.Written -> _value
        | State.WrittenFailure -> raise _exception

and [<Sealed>] internal IVarReadFuture<'a>(ivar: IVar<'a>) =
    inherit IntrusiveNode<IVarReadFuture<'a>>()

    // Current waiter context
    let mutable _context: IContext = nullObj
    member val Context = _context with get, set

    interface Future<'a> with
        member this.Poll(ctx) = ivar.PollResult(this, ctx)

        member this.Cancel() = ivar.CancelRead(this)


module IVar =
    /// Create empty IVar instance
    let inline create () = IVar()

    /// Create future that write result of future in target.
    /// When future throw exception catch it and write in target.
    /// Throw exception when duplicate write in IVar
    let inline write (source: Future<'a>) (ivar: IVar<'a>) = ivar.Write(source)

    /// Put a value and if it is already set raise exception
    let inline writeValue (x: 'a) (ivar: IVar<'a>) = ivar.WriteValue(x)

    let inline writeFailure (ex: exn) (ivar: IVar<'a>) = ivar.WriteException(ex)

    /// <summary> Returns the future pending value. </summary>
    let inline read (ivar: IVar<'a>) = ivar.Read()

    let inline hasValue (ivar: IVar<'a>) : bool = ivar.HasValue()

    let inline get (ivar: IVar<'a>) : 'a = ivar.Get()

