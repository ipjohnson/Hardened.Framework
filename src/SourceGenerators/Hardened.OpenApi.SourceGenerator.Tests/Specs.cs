namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// OpenAPI documents the generator tests are driven with.
///
/// <para>
/// Kept as text rather than as hand-built <c>OpenApiSpecModel</c> instances on purpose: a model built
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
}
