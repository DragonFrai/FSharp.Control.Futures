namespace FSharp.Control.Futures.LowLevel

open FSharp.Control.Futures


/// <summary>
/// A primitive for synchronizing ONE value sender and ONE value receiver
/// </summary>
type [<Struct; NoComparison; NoEquality>] PrimaryOnceCell<'a> =
    val mutable _notify: PrimaryNotify
    val mutable _value: 'a

    internal new (notify: PrimaryNotify, value: 'a) = { _notify = notify; _value = value }
    new ((): unit) = { _notify = PrimaryNotify(false, false); _value = Unchecked.defaultof<'a> }
    static member Empty() = PrimaryOnceCell(PrimaryNotify(false, false), Unchecked.defaultof<'a>)
    static member WithValue(value: 'a) = PrimaryOnceCell(PrimaryNotify(true, false), value)

    member inline this.HasValue = this._notify.IsNotified
    member inline this.Value = this._value

    member inline this.Put(value: 'a) =
        this._value <- value
        if not (this._notify.Notify())
        then this._value <- Unchecked.defaultof<'a>

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let isNotified = this._notify.Poll(ctx)
        match isNotified with
        | false -> NaivePoll.Pending
        | true -> NaivePoll.Ready this._value

    member inline this.Get() : ValueOption<'a> =
        match this.HasValue with
        | false -> ValueNone
        | true -> ValueSome this._value

    member inline this.Drop() =
        if this._notify.Drop()
        then this._value <- Unchecked.defaultof<'a>
