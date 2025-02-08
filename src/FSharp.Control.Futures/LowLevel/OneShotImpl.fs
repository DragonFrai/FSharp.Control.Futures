namespace FSharp.Control.Futures.LowLevel

open FSharp.Control.Futures


[<Class>]
[<Sealed>]
type OneShotImpl<'a> =

    val mutable value: 'a
    val mutable notify: PrimaryNotify

    new(closed: bool) =
        { value = Unchecked.defaultof<'a>
          notify = PrimaryNotify(false, closed) }

    new() =
        OneShotImpl(false)

    member inline this.IsClosed: bool = this.notify.IsTerminated
    member this.Send(msg: 'a): bool = this.SendResult(msg)
    member inline this.Close() : unit = do this.notify.Drop() |> ignore
    member inline this.Await() : Future<'a> = this

    member this.SendResult(result: 'a): bool =
        if this.notify.IsNotified then invalidOp "OneShot already contains value"
        this.value <- result
        let isSuccess = this.notify.Notify()
        if not isSuccess then
            this.value <- Unchecked.defaultof<_>
        isSuccess

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            if this.notify.Poll(ctx)
            then
                let value = this.value
                this.value <- Unchecked.defaultof<'a>
                Poll.Ready value
            else Poll.Pending

        member this.Drop() : unit =
            do this.notify.Drop() |> ignore
