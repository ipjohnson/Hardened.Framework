namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// OpenAPI documents the generator tests are driven with.
///
/// <para>
/// Kept as text rather than as hand-built <c>ServiceSpecModel</c> instances on purpose: a model built
/// in a test can express shapes the parser never produces, and then the emitter is being tested
/// against a spec that cannot occur. Every document here is one a real project could ship.
/// </para>
/// </summary>
internal static class Specs {

    /// <summary>One tag, one operation, one schema — the smallest spec that generates everything.</summary>
    internal const string Minimal =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              responses:
                '200':
                  description: A pet
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    /// <summary>
    /// Every schema shape the type mapper knows: primitives with formats, an enum by reference, an
    /// inline enum, an array of references, and a dictionary from <c>additionalProperties</c>.
    /// </summary>
    internal const string EverySchemaShape =
        """
        openapi: "3.0.0"
        info: { title: Widgets, version: "1.0" }
        paths:
          /widgets:
            get:
              tags: [Widget]
              operationId: listWidgets
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/Widget'
        components:
          schemas:
            WidgetStatus:
              type: string
              enum: [new, used]
            Part:
              type: object
              properties:
                sku: { type: string }
            Widget:
              type: object
              required: [id, count]
              properties:
                id: { type: string }
                count: { type: integer }
                serial: { type: integer, format: int64 }
                price: { type: number, format: double }
                weight: { type: number, format: float }
                inStock: { type: boolean }
                created: { type: string, format: date-time }
                born: { type: string, format: date }
                blob: { type: string, format: byte }
                status:
                  $ref: '#/components/schemas/WidgetStatus'
                parts:
                  type: array
                  items:
                    $ref: '#/components/schemas/Part'
                labels:
                  type: object
                  additionalProperties: { type: string }
                inlineEnum:
                  type: string
                  enum: [a, b]
        """;

    /// <summary>
    /// A body and a query parameter carrying every constraint the validation emitter understands, so
    /// the emitted filter provider exercises all six rule types.
    /// </summary>
    internal const string EveryValidationConstraint =
        """
        openapi: "3.0.0"
        info: { title: Orders, version: "1.0" }
        paths:
          /orders:
            get:
              tags: [Order]
              operationId: listOrders
              parameters:
                - name: limit
                  in: query
                  schema:
                    type: integer
                    minimum: 1
                    maximum: 100
                - name: sort
                  in: query
                  schema:
                    type: string
                    enum: [asc, desc]
                - name: skus
                  in: query
                  schema:
                    type: array
                    minItems: 1
                    maxItems: 10
                    items: { type: string }
              responses:
                '200': { description: ok }
            post:
              tags: [Order]
              operationId: createOrder
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/CreateOrderRequest'
              responses:
                '201':
                  description: created
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Order'
        components:
          schemas:
            Order:
              type: object
              required: [id]
              properties:
                id: { type: string }
            CreateOrderRequest:
              type: object
              required: [sku]
              properties:
                sku:
                  type: string
                  minLength: 3
                  maxLength: 12
                  pattern: "^[A-Z0-9-]+$"
                quantity:
                  type: integer
                  minimum: 1
                  maximum: 999
                discount:
                  type: number
                  minimum: 0
                  exclusiveMinimum: true
                  maximum: 1
                  exclusiveMaximum: true
        """;

    /// <summary>
    /// <c>allOf</c> composition: the derived schema takes its base's properties and required list as
    /// well as its own.
    /// </summary>
    internal const string AllOfComposition =
        """
        openapi: "3.0.0"
        info: { title: Animals, version: "1.0" }
        paths:
          /dogs:
            get:
              tags: [Dog]
              operationId: getDog
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Dog'
        components:
          schemas:
            Animal:
              type: object
              required: [id]
              properties:
                id: { type: string }
                nickname: { type: string }
            Dog:
              allOf:
                - $ref: '#/components/schemas/Animal'
                - type: object
                  required: [breed]
                  properties:
                    breed: { type: string }
        """;

    /// <summary>
    /// Every verb the generator routes, on two paths, so the route tree has a literal node, a
    /// wildcard node and several methods per node.
    /// </summary>
    internal const string EveryVerb =
        """
        openapi: "3.0.0"
        info: { title: Items, version: "1.0" }
        paths:
          /items:
            get:
              tags: [Item]
              operationId: listItems
              responses:
                '200': { description: ok }
            post:
              tags: [Item]
              operationId: createItem
              responses:
                '201': { description: created }
          /items/{itemId}:
            get:
              tags: [Item]
              operationId: getItem
              parameters:
                - name: itemId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200': { description: ok }
            put:
              tags: [Item]
              operationId: replaceItem
              parameters:
                - name: itemId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200': { description: ok }
            patch:
              tags: [Item]
              operationId: updateItem
              parameters:
                - name: itemId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200': { description: ok }
            delete:
              tags: [Item]
              operationId: deleteItem
              parameters:
                - name: itemId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '204': { description: gone }
        """;

    /// <summary>
    /// <c>x-filter-types</c> declares an attribute for the generator to emit; <c>x-filters</c> puts an
    /// instance of it on an operation with property values overridden.
    /// </summary>
    internal const string FilterTypes =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        x-filter-types:
          RateLimit:
            namespace: TestNamespace.Filters
            generate: true
            properties:
              MaxRequests: { type: integer, default: 100 }
              Window: { type: string, default: "minute" }
              Enabled: { type: boolean, default: true }
          External:
            namespace: TestNamespace.Filters
            generate: false
            properties:
              Level: { type: string }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              x-filters:
                RateLimit:
                  MaxRequests: 10
                  Window: second
                  Enabled: false
              responses:
                '200': { description: ok }
        """;

    /// <summary>A tagged operation whose parameter is excluded from generation.</summary>
    internal const string CodegenExcludedParameter =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: traceId
                  in: query
                  schema: { type: string }
                  x-codegen-exclude: true
                - name: page
                  in: query
                  schema: { type: integer }
              responses:
                '200': { description: ok }
        """;

    /// <summary>
    /// Parameters declared on the path item rather than on each operation, which is what OpenAPI
    /// offers for a token every verb on a path shares.
    ///
    /// <para>
    /// Covers the three cases that differ: an operation that only inherits (<c>getPet</c>), one
    /// that redeclares the shared parameter to constrain it (<c>replacePet</c>), and one that adds
    /// its own alongside (<c>listPetToys</c>).
    /// </para>
    /// </summary>
    internal const string PathItemLevelParameters =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            parameters:
              - name: petId
                in: path
                required: true
                schema: { type: string }
            get:
              tags: [Pet]
              operationId: getPet
              responses:
                '200': { description: ok }
            put:
              tags: [Pet]
              operationId: replacePet
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string, maxLength: 36 }
              responses:
                '200': { description: ok }
          /pets/{petId}/toys:
            parameters:
              - name: petId
                in: path
                required: true
                schema: { type: string }
            get:
              tags: [Pet]
              operationId: listPetToys
              parameters:
                - name: limit
                  in: query
                  schema: { type: integer }
              responses:
                '200': { description: ok }
        """;

    /// <summary>
    /// A path-item parameter and an operation parameter sharing a name but not a location.
    ///
    /// <para>
    /// Parsed only. A parameter's identity is name plus location, so these are two parameters and
    /// neither overrides the other — but the generated <c>Parameters</c> class names its members
    /// after the parameter alone, so a spec like this produces CS0102. That collision is
    /// independent of where the declarations came from: an operation declaring both itself fails
    /// the same way. Whichever fix that gets, this stays the parser's answer.
    /// </para>
    /// </summary>
    internal const string ParametersSharingANameAcrossLocations =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            parameters:
              - name: petId
                in: path
                required: true
                schema: { type: string }
            delete:
              tags: [Pet]
              operationId: deletePet
              parameters:
                - name: petId
                  in: header
                  required: true
                  schema: { type: string }
              responses:
                '204': { description: gone }
        """;

    /// <summary>
    /// Prose on an operation and on its parameters, including the shapes that need handling before
    /// they can go after a <c>///</c>: a multi-line description, and one containing characters that
    /// are markup inside an XML doc comment.
    /// </summary>
    internal const string DescribedOperations =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              summary: Lists every pet.
              description: Ignored, because summary is the one-line form.
              parameters:
                - name: limit
                  in: query
                  description: How many to return.
                  schema: { type: integer }
              responses:
                '200': { description: ok }
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              description: |
                Returns a single pet.

                Wraps across lines, and mentions that 0 < limit <= 100 & that ids are opaque.
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200': { description: ok }
          /pets/undocumented:
            get:
              tags: [Pet]
              operationId: undocumentedPets
              responses:
                '200': { description: ok }
        components:
          schemas:
            Pet:
              type: object
              description: |
                A pet in the store.

                Identified by an opaque id.
              required: [id]
              properties:
                id:
                  type: string
                  description: Opaque identifier.
                nickname:
                  type: string
            Status:
              type: string
              description: How far along a pet is.
              enum:
                - available
                - sold
        """;

    /// <summary>
    /// A discriminated hierarchy, written the way OpenAPI writes inheritance: a base carrying the
    /// discriminator and the shared properties, and derived schemas composing it with
    /// <c>allOf</c>.
    /// </summary>
    /// <remarks>
    /// The <c>oneOf</c> on the base names the branches, so the mapping does not have to be written
    /// out. Before this was supported the base parsed as a primitive with no type at all and every
    /// property referencing it became a <c>JsonElement</c>.
    /// </remarks>
    internal const string DiscriminatedHierarchy =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: array
                        items:
                          $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              description: Any animal in the store.
              required: [petType, name]
              properties:
                petType: { type: string }
                name: { type: string }
                nickname: { type: string }
              discriminator:
                propertyName: petType
                mapping:
                  dog: '#/components/schemas/Dog'
                  cat: '#/components/schemas/Cat'
              oneOf:
                - $ref: '#/components/schemas/Dog'
                - $ref: '#/components/schemas/Cat'
            Dog:
              allOf:
                - $ref: '#/components/schemas/Pet'
                - type: object
                  required: [breed]
                  properties:
                    breed: { type: string }
            Cat:
              allOf:
                - $ref: '#/components/schemas/Pet'
                - type: object
                  properties:
                    indoor: { type: boolean }
        """;

    /// <summary>
    /// The four combinations of <c>required</c> and <c>nullable</c>, which are orthogonal in
    /// OpenAPI 3.0 and used to be conflated into one flag.
    /// </summary>
    internal const string RequiredAndNullable =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: requiredPlain
                  in: query
                  required: true
                  schema: { type: string }
                - name: requiredNullable
                  in: query
                  required: true
                  schema: { type: string, nullable: true }
                - name: optionalPlain
                  in: query
                  schema: { type: string }
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Thing'
        components:
          schemas:
            Thing:
              type: object
              required: [requiredPlain, requiredNullable]
              properties:
                requiredPlain: { type: string }
                requiredNullable: { type: string, nullable: true }
                optionalPlain: { type: string }
                optionalNullable: { type: string, nullable: true }
        """;

    /// <summary>A deprecated operation and a deprecated schema.</summary>
    internal const string Deprecated =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              deprecated: true
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/OldThing'
        components:
          schemas:
            OldThing:
              type: object
              deprecated: true
              required: [id]
              properties:
                id: { type: string }
        """;

    /// <summary>
    /// Declared <c>default</c> values, including a type whose default has no constant form in C#.
    /// </summary>
    internal const string DeclaredDefaults =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: limit
                  in: query
                  schema: { type: integer, default: 25 }
                - name: sort
                  in: query
                  schema: { type: string, default: "asc" }
                - name: verbose
                  in: query
                  schema: { type: boolean, default: false }
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Thing'
        components:
          schemas:
            Thing:
              type: object
              required: [id]
              properties:
                id: { type: string }
                label: { type: string, default: "unnamed" }
                size: { type: integer, default: 10 }
                ratio: { type: number, format: double, default: 0.5 }
                enabled: { type: boolean, default: true }
                quoted: { type: string, default: 'a "quoted" value' }
                since: { type: string, format: date-time, default: "2020-01-01T00:00:00Z" }
        """;

    /// <summary>
    /// Objects written inline rather than declared in <c>components/schemas</c>, including one
    /// nested inside another.
    /// </summary>
    internal const string InlineObjects =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: listPets
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              required: [id, address]
              properties:
                id: { type: string }
                address:
                  type: object
                  description: Where the pet lives.
                  required: [city]
                  properties:
                    city:
                      type: string
                      minLength: 2
                    country: { type: string }
                    geo:
                      type: object
                      properties:
                        lat: { type: number, format: double }
                        lon: { type: number, format: double }
        """;

    /// <summary>
    /// Declared error responses, with and without a payload.
    /// </summary>
    /// <summary>
    /// A 201 declaring a Location, a 200 declaring an ETag, and a 429 declaring a Retry-After.
    /// </summary>
    /// <remarks>
    /// The three shapes that matter: a primary success that has to become a wrapper to carry its
    /// header, a second success that was already a wrapper, and an error - because
    /// <c>Retry-After</c> on a 429 is the case RFC 9110 names and an error case is the one place a
    /// header needed no new decision.
    /// </remarks>
    internal const string DeclaredResponseHeaders =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            post:
              tags: [Pet]
              operationId: createPet
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Pet'
              responses:
                '201':
                  description: Created
                  headers:
                    Location:
                      description: Where the new pet can be read.
                      schema: { type: string }
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
                '202':
                  description: Accepted for later creation
                  headers:
                    ETag:
                      schema: { type: string }
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
                '429':
                  description: Too many requests
                  headers:
                    Retry-After:
                      schema: { type: string }
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ApiError'
        components:
          schemas:
            Pet:
              type: object
              properties:
                id: { type: string }
                name: { type: string }
            ApiError:
              type: object
              properties:
                message: { type: string }
        """;

    internal const string DeclaredErrors =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
                '404':
                  description: No pet with that identifier.
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ApiError'
                '409':
                  description: The pet is being modified elsewhere.
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ApiError'
                '503':
                  description: Nothing to say about it.
        components:
          schemas:
            Pet:
              type: object
              required: [id]
              properties:
                id: { type: string }
            ApiError:
              type: object
              required: [code, message]
              properties:
                code: { type: string }
                message: { type: string }
        """;

    /// <summary>
    /// <c>minProperties</c> and <c>maxProperties</c> on a schema that becomes a dictionary.
    /// </summary>
    internal const string PropertyCountBounds =
        """
        openapi: "3.0.0"
        info: { title: Things, version: "1.0" }
        paths:
          /things:
            post:
              tags: [Thing]
              operationId: createThing
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Thing'
              responses:
                '200': { description: ok }
        components:
          schemas:
            Thing:
              type: object
              required: [labels]
              properties:
                labels:
                  type: object
                  minProperties: 1
                  maxProperties: 10
                  additionalProperties: { type: string }
                tags:
                  type: array
                  minItems: 2
                  maxItems: 5
                  items: { type: string }
        """;

    /// <summary>A store-tagged spec, for the multiple-specification cases.</summary>
    internal const string SecondSpecWithADifferentTag =
        """
        openapi: "3.0.0"
        info: { title: Stores, version: "1.0" }
        paths:
          /stores:
            get:
              tags: [Store]
              operationId: listStores
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Store'
        components:
          schemas:
            Store:
              type: object
              required: [id]
              properties:
                id: { type: string }
        """;

    /// <summary>
    /// <c>readOnly</c> and <c>writeOnly</c>, including the combinations that make them awkward.
    /// </summary>
    /// <remarks>
    /// <c>id</c> is the interesting one: required <em>and</em> read-only, which means "always present
    /// in a response" and says nothing a request can be checked against. <c>secret</c> is the mirror.
    /// The <c>Dog</c> hierarchy is here because <c>allOf</c> merges a base's properties into the
    /// derived schema, so a read-only property would otherwise be declared twice.
    /// </remarks>
    internal const string ReadOnlyAndWriteOnly =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets:
            post:
              tags: [Pet]
              operationId: createPet
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Pet'
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              discriminator:
                propertyName: petType
                mapping:
                  dog: '#/components/schemas/Dog'
              oneOf:
                - $ref: '#/components/schemas/Dog'
              required: [id, petType, name]
              properties:
                id:
                  type: string
                  readOnly: true
                  maxLength: 36
                  description: Assigned by the server.
                petType: { type: string }
                name: { type: string, maxLength: 40 }
                secret:
                  type: string
                  writeOnly: true
                tag: { type: string }
            Dog:
              allOf:
                - $ref: '#/components/schemas/Pet'
                - type: object
                  required: [breed]
                  properties:
                    breed: { type: string }
        """;

    /// <summary>A schema whose only property is read-only, so its record has no constructor.</summary>
    internal const string ReadOnlyOnly =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /receipts:
            get:
              tags: [Receipt]
              operationId: getReceipt
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Receipt'
        components:
          schemas:
            Receipt:
              type: object
              required: [id]
              properties:
                id: { type: string, readOnly: true }
        """;

    /// <summary>Valid YAML that is not an OpenAPI document at all.</summary>
    internal const string NotOpenApiYaml =
        """
        version: "3.9"
        services:
          web:
            image: nginx
            ports:
              - "80:80"
        """;

    /// <summary>Valid JSON that is not an OpenAPI document at all.</summary>
    internal const string NotOpenApiJson =
        """
        { "Logging": { "LogLevel": { "Default": "Information" } } }
        """;

    /// <summary>An OpenAPI document written as JSON rather than YAML.</summary>
    internal const string MinimalJson =
        """
        {
          "openapi": "3.0.0",
          "info": { "title": "Pets", "version": "1.0" },
          "paths": {
            "/pets": {
              "get": {
                "tags": ["Pet"],
                "operationId": "listPets",
                "responses": { "200": { "description": "ok" } }
              }
            }
          }
        }
        """;

    /// <summary>
    /// A choice declared as a component of its own, discriminated, plus one on a property so both
    /// declaration sites are generated from one document.
    /// </summary>
    internal const string ComponentChoice =
        """
        openapi: "3.0.0"
        info: { title: Pets, version: "1.0" }
        paths:
          /pets/{petId}:
            get:
              tags: [Pet]
              operationId: getPet
              parameters:
                - name: petId
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              oneOf:
                - $ref: '#/components/schemas/Cat'
                - $ref: '#/components/schemas/Dog'
              discriminator:
                propertyName: kind
            Cat:
              type: object
              required: [kind, meow]
              properties:
                kind: { type: string }
                meow: { type: string }
            Dog:
              type: object
              required: [kind, woof]
              properties:
                kind: { type: string }
                woof: { type: string }
            Sighting:
              type: object
              properties:
                seen:
                  oneOf:
                    - $ref: '#/components/schemas/Cat'
                    - $ref: '#/components/schemas/Dog'
                  discriminator:
                    propertyName: kind
        """;
}
