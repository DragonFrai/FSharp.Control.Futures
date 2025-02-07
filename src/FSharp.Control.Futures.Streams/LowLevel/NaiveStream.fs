namespace FSharp.Control.Futures.Streams.LowLevel

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel.Utils
open FSharp.Control.Futures.Streams


[<Struct>]
[<RequireQualifiedAccess>]
type NaivePollNext<'a> =
    | Pending
    | Next of 'a
    | Completed

/// (TODO: если try без фактического исключения не абсолютно бесплатен, есть смысл убрать его отсюда)
[<Struct; NoComparison; NoEquality>]
type NaiveStream<'a> =

    val mutable public Inner: IStream<'a>

    new(stream: IStream<'a>) = { Inner = stream }

    member inline this.IsNull: bool =
        isNull this.Inner

    member inline this.IsNotNull: bool =
        isNotNull this.Inner

    member inline this.MakeNull() : unit =
        this.Inner <- nullObj

    static member inline Null() : NaiveStream<'a> =
        NaiveStream(nullObj)

    member inline this.PollNext(ctx: IContext) : NaivePollNext<'a> =
        let mutable result = Unchecked.defaultof<_>
        let mutable doLoop = true
        while doLoop do
            let poll =
                try this.Inner.PollNext(ctx)
                with e ->
                    this.Inner <- nullObj
                    reraise ()
            match poll with
            | PollNext.Pending ->
                doLoop <- false
                result <- NaivePollNext.Pending
            | PollNext.Completed ->
                this.Inner <- nullObj
                doLoop <- false
                result <- NaivePollNext.Completed
            | PollNext.Next value ->
                doLoop <- false
                result <- NaivePollNext.Next value
            | PollNext.Transit transitTo ->
                this.Inner <- transitTo
        result

    member inline this.Drop() : unit =
        // Set null before drop call, because drop can throw exception
        let inner = this.Inner
        this.Inner <- nullObj
        inner.Drop()
