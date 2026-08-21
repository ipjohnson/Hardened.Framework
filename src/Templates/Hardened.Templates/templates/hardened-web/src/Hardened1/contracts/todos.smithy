$version: "2"

namespace com.example.hardened1

/// The contract. There are no route attributes in this project - the verb, the path, the shapes
/// and the statuses all come from here, and the build turns them into a service interface, a
/// routing table and the validation the constraints describe.
///
/// Every operation declares its errors as well as its output. That is what a declared response
/// model works from: an operation with no errors generates one return type however the project is
/// configured.
@title("Hardened1 API")
service Todos {
    version: "2024-01-01"
    operations: [GetTodo, CreateTodo, RemoveTodo]
}

structure Todo {
    @required
    id: Integer

    @required
    title: String

    @required
    done: Boolean
}

/// The shape of an error body. @error is what makes it a declared failure rather than an output.
@error("client")
@httpError(404)
structure TodoNotFound {
    @required
    message: String
}

@error("client")
@httpError(409)
structure TodoTitleTaken {
    @required
    message: String
}

@documentation("One todo by id.")
@http(method: "GET", uri: "/todos/{id}", code: 200)
@readonly
operation GetTodo {
    input := {
        @httpLabel
        @required
        @range(min: 1)
        id: Integer
    }

    output: Todo

    errors: [TodoNotFound]
}

@documentation("Creates a todo.")
@http(method: "POST", uri: "/todos", code: 201)
operation CreateTodo {
    input := {
        // The constraint is enforced before the handler runs. Nothing in this project validates
        // anything - the build turns this into a filter in front of the generated handler.
        @required
        @length(min: 1, max: 64)
        title: String
    }

    output: Todo

    errors: [TodoTitleTaken]
}

@documentation("Removes a todo.")
@http(method: "DELETE", uri: "/todos/{id}", code: 204)
@idempotent
operation RemoveTodo {
    input := {
        @httpLabel
        @required
        @range(min: 1)
        id: Integer
    }

    errors: [TodoNotFound]
}
