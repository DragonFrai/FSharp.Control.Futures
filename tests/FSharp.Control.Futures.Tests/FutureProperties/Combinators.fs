module FSharp.Control.Futures.Tests.Properties.Combinators

open System
open Expecto
open FsCheck

open FSharp.Control.Futures


module Gen =

    type FunctorInt2String = FunctorInt2String of (int -> string)
    type FunctorInt2StringGen() =
        static member FunctorInt2String() =
            let genFunctorInt2String: Gen<FunctorInt2String> = gen {
                let! postfix = Gen.choose (0, 1000)
                let! infix = Gen.elements [ "_"; "-"; "~" ]
                let f i =
                    sprintf "%i%s%i" i infix postfix
                return FunctorInt2String f
            }
            Arb.fromGen genFunctorInt2String

    type FutureIntProvider = FutureIntProvider of (unit -> IComputationTmp<int>)
    type FutureIntGen() =
        static member FutureInt() =
            let genFutureInt: Gen<FutureIntProvider> = gen {
                let! i = Gen.choose (Int32.MinValue + 1, Int32.MaxValue)
                let futProvider () = future {
                    return i
                }
                return FutureIntProvider futProvider
            }
            Arb.fromGen genFutureInt

    let addToConfig (config: FsCheckConfig) =
        { config with
            arbitrary =
                typeof<FunctorInt2StringGen>
                :: typeof<FutureIntGen>
                :: config.arbitrary
        }

//    let futureIntArb =


module Expect =
    module Future =
        let equal (actual: IComputationTmp<'a>) (expected: IComputationTmp<'a>) (message: string) : unit =
            let actualResult = Future.runSync actual
            let expectedResult = Future.runSync expected
            Expect.equal actualResult expectedResult message


[<AutoOpen>]
module TestHelpers =
    let private config = Gen.addToConfig FsCheckConfig.defaultConfig
    let testProp name = testPropertyWithConfig config name


let futureEquals fut1 fut2 =
    let r1 = Future.runSync fut1
    let r2 = Future.runSync fut2
    r1 = r2


[<AutoOpen>]
module HaskellNaming =
    let pure' x = Future.ready x
    let fmap f x = Future.map f x
    let return' x = Future.ready x
    let ( <*> ) f fut = Future.apply f fut
    let ( >== ) x f = Future.bind f x

[<Tests>]
let properties = testList "Combinator properties" [

    testProp "fmap f = bind (return . f)" <| fun (Gen.FutureIntProvider (futf: unit -> IComputationTmp<int>)) (Gen.FunctorInt2String (mapping: int -> string)) ->
        let rFut1: IComputationTmp<string> = Future.bind (mapping >> Future.ready) (futf ())
        let rFut2: IComputationTmp<string> = Future.map mapping (futf ())
        futureEquals rFut1 rFut2

    testProp "Homomorphism: pure f <*> pure x = pure (f x)" <| fun (x: int) (Gen.FunctorInt2String (f: int -> string)) ->
        let r1 = pure' f <*> pure' x
        let r2 = pure' (f x)
        futureEquals r1 r2

    testProp "fmap f x = pure f <*> x" <| fun (Gen.FutureIntProvider (f'x: unit -> IComputationTmp<int>)) (Gen.FunctorInt2String (f: int -> string)) ->
        let x1, x2 = f'x (), f'x ()
        let r1 = fmap f x1
        let r2 = pure' f <*> x2
        futureEquals r1 r2

    testProp "(return x) >>= f = f x" <| fun (x: int) (Gen.FunctorInt2String (f: int -> string)) ->
        let f = f >> Future.ready // TODO: Take from arguments
        let r1 = (return' x) >== f
        let r2 = f x
        futureEquals r1 r2
]
