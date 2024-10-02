namespace FSharp.Control.Futures.LowLevel

open FSharp.Control.Futures



type EmptyFeatureProvider private () =
    static member Instance = EmptyFeatureProvider()
    interface IFeatureProvider with
        member this.GetFeature() = ValueNone

[<AbstractClass>]
type NotFeaturedContext() =
    abstract Wake: unit -> unit
    interface IContext with
        member this.Wake() = this.Wake()
        member this.Features() = EmptyFeatureProvider.Instance
