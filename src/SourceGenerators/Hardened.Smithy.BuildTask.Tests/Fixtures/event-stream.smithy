$version: "2"

namespace com.example.events

// A service with one streamed operation, for the parser tests: the payload is a union under
// @streaming, which is Smithy's own spelling for an event stream.
service Events {
    version: "2024-01-01"
    operations: [GetPet, PetEvents]
}

structure Pet {
    @required
    id: String
}

structure PetAdopted {
    @required
    petId: String
}

structure PetWeighed {
    @required
    grams: Integer
}

@streaming
union PetEventStream {
    adopted: PetAdopted
    @jsonName("weighed-in")
    weighed: PetWeighed
}

@http(method: "GET", uri: "/pets/{petId}", code: 200)
@readonly
operation GetPet {
    input := {
        @httpLabel
        @required
        petId: String
    }
    output: Pet
}

@http(method: "GET", uri: "/pets/{petId}/events", code: 200)
@readonly
operation PetEvents {
    input := {
        @httpLabel
        @required
        petId: String
    }
    output := {
        @required
        @httpPayload
        events: PetEventStream
    }
}
