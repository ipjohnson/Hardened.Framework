$version: "2"
namespace com.example.bank

use aws.protocols#awsJson1_0

@awsJson1_0
service Bank {
    version: "2024-01-01"
    operations: [GetBalance, Transfer]
}

@readonly
operation GetBalance {
    input := {
        @required
        accountId: String
    }
    output := {
        @required
        balanceCents: Long
        currency: String
    }
    errors: [AccountNotFound]
}

operation Transfer {
    input := {
        @required
        fromAccount: String

        @required
        toAccount: String

        @required
        @range(min: 1)
        amountCents: Long
    }
    output := {
        @required
        transferId: String
    }
    errors: [AccountNotFound]
}

@error("client")
structure AccountNotFound {
    @required
    message: String
}
