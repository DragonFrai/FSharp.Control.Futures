[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Channel


let toPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> IReceiver<'a>
