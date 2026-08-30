# Validation System Usage Guide

## OpenAPI Constraint Mapping

Add standard OpenAPI validation constraints to your spec. The source generator automatically creates validation filters.

### Supported Constraints

| OpenAPI Constraint | Rule | Applies To |
|---|---|---|
| `required` | `RequiredRule` | All types |
| `minLength` / `maxLength` | `StringLengthRule` | Strings |
| `minimum` / `maximum` | `RangeRule` | Numbers |
| `exclusiveMinimum` / `exclusiveMaximum` | `RangeRule` | Numbers |
| `pattern` | `PatternRule` | Strings |
| `enum` | `EnumRule` | Strings |
| `minItems` / `maxItems` | `ArrayBoundsRule` | Arrays |

### Example Spec

```yaml
components:
  schemas:
    CreatePetRequest:
      type: object
      required:
        - name
      properties:
        name:
          type: string
          minLength: 1
          maxLength: 100
        age:
          type: integer
          minimum: 0
          maximum: 30
        tag:
          type: string
          pattern: "^[a-zA-Z0-9-]+$"
        status:
          type: string
          enum:
            - available
            - pending
            - sold
        nicknames:
          type: array
          items:
            type: string
          minItems: 0
          maxItems: 5
paths:
  /pets:
    post:
      operationId: createPet
      parameters:
        - name: limit
          in: query
          schema:
            type: integer
            minimum: 1
            maximum: 100
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreatePetRequest'
```

## Custom Validators

For business-logic validation beyond OpenAPI constraints:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Validation;

[TransientService]
public class CreatePetValidator : ICustomRequestValidator<CreatePetRequest> {
    private readonly IPetRepository _repository;

    public CreatePetValidator(IPetRepository repository) {
        _repository = repository;
    }

    public async Task<ValidationResult> ValidateAsync(CreatePetRequest request) {
        var result = new ValidationResult();

        if (await _repository.ExistsAsync(request.Name)) {
            result.AddError("name", "duplicate", "A pet with this name already exists.");
        }

        return result;
    }
}
```

Custom validators:
- Are resolved from DI (register with `[TransientService]`).
- Run **after** built-in OpenAPI rule validation.
- Support constructor injection for dependencies.
- Multiple validators per request type are aggregated.

## Error Response

When validation fails, the filter returns HTTP 400 with a structured response:

```json
{
  "type": "ValidationError",
  "message": "One or more validation errors occurred.",
  "errors": [
    {
      "field": "name",
      "code": "required",
      "message": "name is required."
    },
    {
      "field": "age",
      "code": "range",
      "message": "age must be between 0 and 30."
    }
  ]
}
```

## Programmatic Validation

You can also throw `ValidationException` from handler code:

```csharp
var result = new ValidationResult();
result.AddError("email", "invalid", "Email format is not valid.");
throw new ValidationException(result);
```

The `ExceptionToModelConverter` will convert this to the same structured 400 response.
