$version: "2"
namespace com.example.petstore

@title("Pet Store")
// The scheme is declared on the service, so every operation requires a caller to authenticate
// unless it opts out. The three below opt out with @auth([]) and stay public; GetSecuredPet does
// not, and is the arm of the front-end conformance suite that covers authorization.
//
// Smithy has no scopes, so it can require authentication and never a particular grant - which is
// why the shared assertion is that a secured route refuses an anonymous caller, and not that it
// demands pets:read the way the OpenAPI and code-first fixtures can.
@httpBearerAuth
service PetStore {
    version: "2024-01-01"
    operations: [GetPet, ListPets, CreatePet, GetSecuredPet]
}

@documentation("Requires an authenticated caller.")
@http(method: "GET", uri: "/pets/secured", code: 200)
@readonly
operation GetSecuredPet {
    output := {
        @required
        pet: Pet
    }
}

@documentation("Fetch one pet by id.")
@http(method: "GET", uri: "/pets/{petId}", code: 200)
@auth([])
@readonly
operation GetPet {
    input := {
        @httpLabel
        @required
        @pattern("^[a-z0-9-]+$")
        petId: String

        @httpQuery("verbose")
        detailed: Boolean

        @httpHeader("X-Trace-Id")
        traceId: String
    }
    output := {
        @required
        pet: Pet
    }
    errors: [PetNotFound, Throttled]
}

@http(method: "GET", uri: "/pets", code: 200)
@auth([])
@readonly
operation ListPets {
    input := {
        @httpQuery("limit")
        @range(min: 1, max: 100)
        limit: Integer
    }
    output := {
        @required
        pets: PetList

        nextToken: String
    }
}

@auth([])
@http(method: "POST", uri: "/pets", code: 201)
operation CreatePet {
    input := {
        @required
        @length(min: 1, max: 64)
        name: String

        kind: PetKind

        tags: TagMap

        birthday: Timestamp

        @jsonName("photo_bytes")
        photo: Blob
    }
    output := {
        @required
        pet: Pet
    }
    errors: [PetNotFound]
}

@documentation("A pet.")
structure Pet {
    @required
    id: String

    @required
    name: String

    kind: PetKind

    @deprecated
    nickname: String

    attributes: Attribute

    metadata: Document
}

union Attribute {
    weightKg: Double
    note: String
}

enum PetKind {
    DOG = "dog"
    CAT = "cat"
    OTHER = "other"
}

list PetList {
    member: Pet
}

map TagMap {
    key: String
    value: String
}

@error("client")
@httpError(404)
structure PetNotFound {
    @required
    message: String
}

@error("client")
@httpError(429)
structure Throttled {
    message: String

    @range(min: 0)
    retryAfterSeconds: Integer
}
