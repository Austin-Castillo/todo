using Microsoft.AspNetCore.Http.HttpResults;
using Todo.Contracts;

namespace Todo.Api.Endpoints;

// Thin HTTP layer: ITodoService (ServerTodoService here) holds the data access; these map its results to status codes.
public static class TodoListEndpoints
{
    public static IEndpointRouteBuilder MapTodoListEndpoints(this IEndpointRouteBuilder api)
    {
        var lists = api.MapGroup("/lists");

        lists.MapGet("/", GetLists);
        lists.MapGet("/{id:int}", GetList);
        lists.MapPost("/", CreateList);
        lists.MapPut("/{id:int}", UpdateList);
        lists.MapDelete("/{id:int}", DeleteList);

        return api;
    }

    private static async Task<Ok<List<TodoListSummary>>> GetLists(ITodoService todos, CancellationToken ct) =>
        TypedResults.Ok(await todos.GetListsAsync(ct));

    private static async Task<Results<Ok<TodoListDetails>, NotFound>> GetList(
        int id, ITodoService todos, CancellationToken ct)
    {
        var list = await todos.GetListAsync(id, ct);

        return list is null ? TypedResults.NotFound() : TypedResults.Ok(list);
    }

    private static async Task<Created<TodoListDetails>> CreateList(
        CreateTodoListRequest request, ITodoService todos, CancellationToken ct)
    {
        // The server implementation always saves a valid request (validation ran before this handler).
        var list = await todos.CreateListAsync(request, ct)
            ?? throw new InvalidOperationException("The list wasn't created.");

        return TypedResults.Created($"/api/lists/{list.Id}", list);
    }

    private static async Task<Results<NoContent, NotFound>> UpdateList(
        int id, UpdateTodoListRequest request, ITodoService todos, CancellationToken ct) =>
        await todos.UpdateListAsync(id, request, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<NoContent, NotFound>> DeleteList(
        int id, ITodoService todos, CancellationToken ct) =>
        await todos.DeleteListAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
}
