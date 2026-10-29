namespace Cotabby.Core.CLI;

// Static traditional definitions from 3b LingroveCLI/CLIInputSchema.swift.
internal static class CliLegacySchema
{
    internal const string Json = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Lingrove vocabulary batch v1",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "version": {
      "const": 1
    },
    "idempotencyKey": {
      "type": "string",
      "minLength": 1,
      "maxLength": 200
    },
    "operations": {
      "type": "array",
      "minItems": 1,
      "maxItems": 100,
      "items": {
        "oneOf": [
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "add": {
                "$ref": "#/$defs/add"
              }
            },
            "required": [
              "add"
            ]
          },
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "append": {
                "$ref": "#/$defs/append"
              }
            },
            "required": [
              "append"
            ]
          },
          {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "update": {
                "$ref": "#/$defs/update"
              }
            },
            "required": [
              "update"
            ]
          }
        ]
      }
    }
  },
  "required": [
    "version",
    "idempotencyKey",
    "operations"
  ],
  "$defs": {
    "range": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "location": {
          "type": "integer",
          "minimum": 0
        },
        "length": {
          "type": "integer",
          "minimum": 1
        }
      },
      "required": [
        "location",
        "length"
      ]
    },
    "example": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "text": {
          "type": "string",
          "minLength": 1
        },
        "translation": {
          "type": "string"
        },
        "translationLanguage": {
          "type": "string"
        },
        "meaning": {
          "type": "string"
        },
        "meaningLanguage": {
          "type": "string"
        },
        "completeness": {
          "enum": [
            "sentence",
            "fragment"
          ]
        },
        "ranges": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/range"
          }
        }
      },
      "required": [
        "text"
      ]
    },
    "block": {
      "oneOf": [
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "text": {
              "type": "string",
              "minLength": 1
            },
            "bold": {
              "type": "boolean"
            },
            "italic": {
              "type": "boolean"
            }
          },
          "required": [
            "text"
          ]
        },
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "image": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "path": {
                  "type": "string",
                  "minLength": 1
                },
                "caption": {
                  "type": "string"
                }
              },
              "required": [
                "path"
              ]
            }
          },
          "required": [
            "image"
          ]
        }
      ]
    },
    "add": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "expression": {
          "type": "string",
          "minLength": 1
        },
        "dictionaryForm": {
          "type": "string",
          "minLength": 1
        },
        "language": {
          "type": "string",
          "minLength": 1
        },
        "kind": {
          "enum": [
            "word",
            "expression",
            "pattern",
            "sentence"
          ]
        },
        "meaning": {
          "type": "string",
          "minLength": 1
        },
        "meaningLanguage": {
          "type": "string"
        },
        "note": {
          "type": "string"
        },
        "tags": {
          "type": "array",
          "maxItems": 100,
          "items": {
            "type": "string",
            "minLength": 1,
            "maxLength": 200
          }
        },
        "examples": {
          "type": "array",
          "maxItems": 100,
          "items": {
            "$ref": "#/$defs/example"
          }
        },
        "content": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/block"
          }
        }
      },
      "required": [
        "expression",
        "language",
        "kind",
        "meaning"
      ]
    },
    "append": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "entryID": {
          "type": "string",
          "format": "uuid"
        },
        "senseID": {
          "type": "string",
          "format": "uuid"
        },
        "tags": {
          "type": "array",
          "maxItems": 100,
          "items": {
            "type": "string",
            "minLength": 1,
            "maxLength": 200
          }
        },
        "examples": {
          "type": "array",
          "maxItems": 100,
          "items": {
            "$ref": "#/$defs/example"
          }
        },
        "content": {
          "type": "array",
          "maxItems": 64,
          "items": {
            "$ref": "#/$defs/block"
          }
        }
      },
      "required": [
        "entryID",
        "senseID"
      ]
    },
    "update": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "entryID": {
          "type": "string",
          "format": "uuid"
        },
        "senseID": {
          "type": "string",
          "format": "uuid"
        },
        "expectedRevision": {
          "type": "integer",
          "minimum": 0
        },
        "meaning": {
          "type": "string",
          "minLength": 1
        },
        "meaningLanguage": {
          "type": "string"
        },
        "note": {
          "type": "string"
        },
        "tags": {
          "type": "array",
          "maxItems": 100,
          "items": {
            "type": "string",
            "minLength": 1,
            "maxLength": 200
          }
        }
      },
      "required": [
        "entryID",
        "senseID",
        "expectedRevision"
      ],
      "anyOf": [
        {
          "required": [
            "meaning"
          ]
        },
        {
          "required": [
            "meaningLanguage"
          ]
        },
        {
          "required": [
            "note"
          ]
        },
        {
          "required": [
            "tags"
          ]
        }
      ]
    }
  }
}
""";
}
