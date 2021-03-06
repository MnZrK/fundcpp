[<AutoOpen>]
module FDCUtil.Net

open System
open System.Net.Sockets

[<AutoOpen>]
module SocketExtensions =
    type System.Net.Sockets.Socket with
        member socket.AsyncAccept() = Async.FromBeginEnd(socket.BeginAccept, socket.EndAccept)
        member socket.AsyncReceive(buffer:byte[], ?offset, ?count) =
            let offset = defaultArg offset 0
            let count = defaultArg count buffer.Length
            let beginReceive(b,o,c,cb,s) = socket.BeginReceive(b,o,c,SocketFlags.None,cb,s)
            Async.FromBeginEnd(buffer, offset, count, beginReceive, socket.EndReceive)
        member socket.AsyncSend(buffer:byte[], ?offset, ?count) =
            let offset = defaultArg offset 0
            let count = defaultArg count buffer.Length
            let beginSend(b,o,c,cb,s) = socket.BeginSend(b,o,c,SocketFlags.None,cb,s)
            Async.FromBeginEnd(buffer, offset, count, beginSend, socket.EndSend)

let fetch_concat_split_from_socket buffer_size som_marker_maybe eom_marker (socket: System.Net.Sockets.Socket) =
    let read () = 
        let buffer = Array.zeroCreate buffer_size
        socket.AsyncReceive(buffer, 0, buffer_size)
        |> Async.Catch
        |> AsyncResult.ofAsyncChoice2
        |> AsyncResult.map (fun bytes_read -> 
            if bytes_read <= 0 then
                [||]
            else
                printfn "raw receiving %A" (System.Text.Encoding.ASCII.GetString (buffer.[0..bytes_read-1]))
                buffer.[0..bytes_read-1] 
        )

    Array.fetchConcatSplit som_marker_maybe eom_marker read 

let concat_and_split_stream buffer_size eom_marker (stream: IO.Stream) =
    let rec read() = seq {
        let buffer = Array.zeroCreate buffer_size
        let bytes_read = stream.Read(buffer, 0, buffer_size)
        if bytes_read <= 0 then 
            ()
        else 
            yield buffer.[0..bytes_read-1]
            yield! read()
    }
    Array.concatSplit eom_marker (read()) 
