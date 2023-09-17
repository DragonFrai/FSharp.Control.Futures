namespace rec FSharp.Control.Futures.Lock

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


exception IVarDoublePutException
exception IVarEmptyException

[<Struct; RequireQualifiedAccess>]
type internal State<'a> =
    | Blank
    | Written
    | WrittenFailure

/// <summary>
/// An immutable cell to asynchronously wait for a single value.
/// Represents the pending future in which value can be put.
/// If you never put a value, you will endlessly wait for it.
/// Provides a mechanism for dispatching exceptions. It can be expressed through Result and is not recommended.
/// Of course, unless you cannot live without exceptions or you need to express a really critical case,
/// which is too expensive to wrap in "(x: IVar<Result<_, _>>) |> Future.raise".
/// </summary>
type [<Sealed>] IVar<'a> =

    val internal spinLock: SpinLock
    val mutable internal state: State<'a>
    val mutable internal value: 'a
    val mutable internal exnValue: exn
    val mutable internal waiters: IntrusiveList<IVarGetFuture<'a>> // not empty only on Blank state

    new() =
        { spinLock = SpinLock(false)
          state = State.Blank
          value = Unchecked.defaultof<'a>
          exnValue = nullObj
          waiters = IntrusiveList.Create() }

    member this.IsFull: bool = this.state <> State.Blank

    member this.IsEmpty: bool = this.state = State.Blank

    member this.PutExn(ex: exn) =
        Impl.Put(this, Unchecked.defaultof<'a>, ex)

    member this.Put(value: 'a) =
        Impl.Put(this, value, nullObj)

    member this.Take(): 'a =
        match this.state with
        | State.Written -> this.value
        | State.WrittenFailure -> raise this.exnValue
        | State.Blank -> raise IVarEmptyException

    member this.Get() : Future<'a> =
        upcast IVarGetFuture(this)

    member this.TryGet() : 'a option =
        if this.IsFull then Some (this.Take()) else None

type [<Sealed>] internal IVarGetFuture<'a> =
    inherit IntrusiveNode<IVarGetFuture<'a>>

    val mutable internal ivar: IVar<'a>
    val mutable internal notify: PrimaryNotify

    new (ivar: IVar<'a>) =
        { inherit IntrusiveNode<IVarGetFuture<'a>>(); ivar = ivar; notify = PrimaryNotify(false) }

    interface Future<'a> with
        member this.Poll(ctx) = Impl.PollGet(this.ivar, this, ctx)
        member this.Drop() = Impl.DropGet(this.ivar, this)

// TODO: Make Impl members inline
type [<Sealed>] internal Impl =
    static member inline internal PutNoSync(ivar: IVar<'a>, value: 'a, ex: exn) : unit =
        if isNull ex
        then
            ivar.value <- value
            ivar.state <- State.Written
        else
            ivar.exnValue <- ex
            ivar.state <- State.WrittenFailure

    static member internal Put(ivar: IVar<'a>, value: 'a, ex: exn) : unit =
        let mutable hasLock = false
        ivar.spinLock.Enter(&hasLock)
        match ivar.state with
        | State.Blank ->
            Impl.PutNoSync(ivar, value, ex)
            let root = ivar.waiters.Drain()
            if hasLock then ivar.spinLock.Exit()
            IntrusiveNode.forEach (fun (fut: IVarGetFuture<'a>) -> fut.notify.Notify() |> ignore) root
        | State.Written | State.WrittenFailure ->
            if hasLock then ivar.spinLock.Exit()
            raise IVarDoublePutException

    static member internal PollGet(ivar: IVar<'a>, reader: IVarGetFuture<'a>, ctx: IContext) : Poll<'a> =
        match ivar.state with
        | State.Written -> Poll.Ready ivar.value
        | State.WrittenFailure -> raise ivar.exnValue
        | State.Blank ->
            let mutable hasLock = false
            ivar.spinLock.Enter(&hasLock)
            match ivar.state with
            | State.Written ->
                if hasLock then ivar.spinLock.Exit()
                Poll.Ready ivar.value
            | State.WrittenFailure ->
                if hasLock then ivar.spinLock.Exit()
                raise ivar.exnValue
            | State.Blank ->
                try
                    if reader.notify.Poll(ctx) then unreachable ()
                    ivar.waiters.PushBack(reader)
                    Poll.Pending
                finally
                    if hasLock then ivar.spinLock.Exit()

    static member internal DropGet(ivar: IVar<'a>, reader: IVarGetFuture<'a>) : unit =
        if reader.notify.IsWaiting then
            let mutable hasLock = false
            ivar.spinLock.Enter(&hasLock)
            reader.notify.Drop() |> ignore
            ivar.waiters.Remove(reader) |> ignore
            if hasLock then ivar.spinLock.Exit()
        else
            reader.notify.Drop() |> ignore

module IVar =
    /// Create empty IVar instance
    let inline create () : IVar<'a> = IVar()

    /// Put a value and if it is already set raise exception
    let inline put (x: 'a) (ivar: IVar<'a>) : unit = ivar.Put(x)

    let inline putExn (ex: exn) (ivar: IVar<'a>) : unit = ivar.PutExn(ex)

    /// Create future that write result of future in target.
    /// When future throw exception catch it and write in target.
    /// Throw exception when duplicate write in IVar
    let inline pass (source: Future<'a>) (ivar: IVar<'a>) : Future<unit> = future {
        try
            let! x = source
            put x ivar
        with e ->
            putExn e ivar
    }

    /// <summary> Returns the future pending value. </summary>
    let inline get (ivar: IVar<'a>) : Future<'a> = ivar.Get()

    /// <summary> Returns the value or throw exn </summary>
    let inline tryGet (ivar: IVar<'a>) : 'a option = ivar.TryGet()

    let inline isFull (ivar: IVar<'a>) : bool = ivar.IsFull

    let inline take (ivar: IVar<'a>) : 'a = ivar.Take()
