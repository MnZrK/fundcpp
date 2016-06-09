module FDCDomain.MessageQueue.Tests

open Xunit
open Swensen.Unquote

open FDCUtil.Main
open FDCDomain.MessageQueue

module ``Utilities Tests`` = 

    [<Fact>]
    let ``Should get bytes from ASCII string`` () =
        test <@ getBytes "123" = [| 49uy; 50uy; 51uy |] @>
            
    [<Fact>]
    let ``Should get string from ASCII bytes`` () =
        test <@ getString [| 49uy; 50uy; 51uy |] = "123" @>
            
    [<Fact>]
    let ``Should convert dangerous characters to DCN`` () =
        test <@ 
                "12$3" 
                |> getBytes 
                |> Array.collect byteToDCN 
                |> getString = "12/%DCN036%/3" 
            @>
        
    [<Fact>]
    let ``Should not touch input wihout dangerous characters`` () =
        let input = "123abbss"

        test <@ 
                input 
                |> getBytes 
                |> Array.collect byteToDCN 
                |> getString = input 
            @>

    [<Fact>]
    let ``Should convert the same for bytes and strings`` () =
        let inputStr = "123abbss"
        let inputBytes = getBytes "123abbss"

        test <@ 
                inputBytes
                |> Array.collect byteToDCN
                |> getString = stringToDCN inputStr 
            @>

    [<Fact>]
    let ``Should convert DCN string back to normal`` () =
        let input = "12/%DCN036%/3" 

        let expected = "12$3"

        test <@ DCNtoString input = expected @>

    [<Fact>]
    let ``Should convert DCN back and worth without change`` () =
        let input = "12$3|\01235ffffaa"

        test <@ "12$3|\01235ffffaa" |> stringToDCN |> DCNtoString = "12$3|\01235ffffaa" @>

module ``LockData Tests`` =

    [<Fact>]
    let ``Should create lock from correct input`` () =
        let input = "12312351afdsfasdfqvz"

        test <@ LockData.create input = Success (LockData.LockData input) @>

    [<Fact>]
    let ``Should not create lock from invalid input`` () =
        test <@ LockData.create null = Failure StringError.Missing @>
        test <@ LockData.create "" = Failure (StringError.MustNotBeShorterThan 2) @>
        test <@ LockData.create "a" = Failure (StringError.MustNotBeShorterThan 2) @>
        test <@ LockData.create "1" = Failure (StringError.MustNotBeShorterThan 2) @>

module ``KeyData Tests`` =

    [<Fact>]
    let ``Should create key from lock`` () =
        let parseHexKey (str: string) = str.Split [|' '|] |> Array.map (fun x -> System.Byte.Parse(x, System.Globalization.NumberStyles.AllowHexSpecifier))

        let input = [
            ("1234", [|51uy; 48uy; 16uy; 112uy|]);
            ("1234asbasdf121\0||mam```341231", [|99uy; 48uy; 16uy; 112uy; 85uy; 33uy; 17uy; 48uy; 33uy; 113uy; 32uy; 117uy;
        48uy; 48uy; 214uy; 198uy; 196uy; 47uy; 37uy; 68uy; 67uy; 78uy; 48uy; 48uy;
        48uy; 37uy; 47uy; 17uy; 192uy; 192uy; 208uy; 47uy; 37uy; 68uy; 67uy; 78uy;
        48uy; 48uy; 48uy; 37uy; 47uy; 47uy; 37uy; 68uy; 67uy; 78uy; 48uy; 48uy; 48uy;
        37uy; 47uy; 53uy; 112uy; 80uy; 48uy; 16uy; 32uy|]);
            ("EXTENDEDPROTOCOLSbWZ4Y^UXrsJBbhd=yxeVJlGdd8wg6", parseHexKey "11 d1 c0 11 b0 a0 10 10 41 20 d1 b1 b1 c0 c0 30 f1 13 53 d0 e6 d6 70 b0 d0 a2 10 93 80 02 a0 c0 95 44 10 d1 33 c1 62 b2 32 2f 25 44 43 4e 30 30 30 25 2f c5 f4 01 15")
        ]

        test <@ input 
                |> List.filter (fun (str, bytes) -> 
                    Result.mapSuccess KeyData.create (LockData.create str) <> Success bytes
                ) = []
            @>
