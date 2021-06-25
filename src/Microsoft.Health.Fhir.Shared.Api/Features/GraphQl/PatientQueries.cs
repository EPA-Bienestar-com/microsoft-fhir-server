﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using HotChocolate.Types;
using Microsoft.Health.Fhir.Api.Features.GraphQl.DataLoader;

namespace Microsoft.Health.Fhir.Shared.Api.Features.GraphQl
{
    [ExtendObjectType(Name = "Query")]
#pragma warning disable CA1041 // Provide ObsoleteAttribute message
    [System.Obsolete]
#pragma warning restore CA1041 // Provide ObsoleteAttribute message
    public class PatientQueries
    {
        public Task<Patient> GetPatientByIdAsync(
            string id,
            PatientByIdDataLoader patientById,
            CancellationToken cancellationToken) => patientById.LoadAsync(id, cancellationToken);

        public async Task<IEnumerable<Patient>> GetPatientsByIdAsync(
            string[] ids,
            PatientByIdDataLoader patientById,
            CancellationToken cancellationToken) => await patientById.LoadAsync(ids, cancellationToken);
    }
}