$version: "2"

namespace com.example.hardened1

/// The contract. There are no route attributes in this project - the verb, the path and the
/// shapes all come from here, and the build turns them into a service interface, a routing table
/// and the validation the constraints describe.
@title("Hardened1 API")
service Greeting {
    version: "2024-01-01"
    operations: [Hello]
}

@documentation("Greets someone by name.")
@http(method: "GET", uri: "/greeting/{name}", code: 200)
@readonly
operation Hello {
    input := {
        @httpLabel
        @required
        @length(min: 1, max: 64)
        name: String
    }

    output := {
        @required
        message: String
    }
}
