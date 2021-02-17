[<RequireQualifiedAccess>]
module FSharp.Control.Futures.Channels.Channel


let inline toPair (ch: IChannel<'a>) = ch :> ISender<'a>, ch :> IReceiver<'a>
