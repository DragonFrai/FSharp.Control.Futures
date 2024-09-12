namespace rec FSharp.Control.Futures.Actors

open System
open FSharp.Control.Futures.Sync


[<Interface>]
type IMsgVisitor =
    abstract Visit<'i, 'o> : msg: Msg<'i, 'o> -> unit

[<Interface>]
type IMsgVisitorFunc<'a, 'r> =
    abstract Visit<'i, 'o> : msg: Msg<'i, 'o> * arg: 'a -> 'r


[<AbstractClass>]
type DynMsg internal () =
    abstract MsgType: Type
    abstract ReplyType: Type
    abstract Accept : IMsgVisitor -> unit
    abstract Accept<'a, 'r> : visitorFunc: IMsgVisitorFunc<'a, 'r> * arg: 'a -> 'r
    abstract Cast<'i, 'o> : unit -> Msg<'i, 'o>

[<Class>]
[<Sealed>]
type Msg<'i, 'o> =
    val Msg: 'i
    val Reply: IOneShotSink<'o>

    new(msg, reply) =
        { inherit DynMsg(); Msg = msg; Reply = reply }

    inherit DynMsg with

        override this.MsgType: Type =
            typeof<'i>

        override this.ReplyType: Type =
            typeof<'o>

        override this.Accept(visitor: IMsgVisitor): unit =
            visitor.Visit<'i, 'o>(this)

        override this.Accept<'a, 'r>(visitorFunc: IMsgVisitorFunc<'a, 'r>, arg: 'a): 'r =
            visitorFunc.Visit<'i, 'o>(this, arg)

        override this.Cast<'m, 'r>(): Msg<'m, 'r> =
            unbox this

[<RequireQualifiedAccess>]
module Msg =
    let create (msg: 'i) (reply: OneShot<'o>) : Msg<'i, 'o> =
        Msg<'i, 'o>(msg, reply)

    let createDyn (msg: 'i) (reply: OneShot<'o>) : DynMsg =
        Msg<'i, 'o>(msg, reply)

    let box (msg: Msg<'i, 'o>) : DynMsg =
        msg

    let unbox<'i, 'o> (msgBox: DynMsg) : Msg<'i, 'o> =
        msgBox.Cast<'i, 'o>()
