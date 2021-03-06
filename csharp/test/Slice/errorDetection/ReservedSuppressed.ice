//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

[[suppress-warning(reserved-identifier)]]

#include <include/IcePrefix.ice>

module Ice {}
module IceFoo {}

module all::good::here {}
module an::iceberg::ahead {}
module aPtr::okay::bPrx::fine::cHelper {}

module _a {}           // Illegal leading underscore
module _true {}        // Illegal leading underscore
module \_true {}       // Illegal leading underscore

module b_ {}           // Illegal trailing underscore

module b__c {}         // Illegal double underscores
module b___c {}        // Illegal double underscores

module _a_ {}          // Illegal underscores
module a_b {}          // Illegal underscore
module a_b_c {}        // Illegal underscores
module _a__b__ {}      // Illegal underscores
