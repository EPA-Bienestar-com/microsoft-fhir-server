﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GreenDonut;
using Hl7.Fhir.Model;
using HotChocolate.DataLoader;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.GraphQl;

namespace Microsoft.Health.Fhir.Api.Features.GraphQl.DataLoader
{
    public class PatientByIdDataLoader : BatchDataLoader<string, Patient>
    {
        private readonly IMediator _mediator;

        public PatientByIdDataLoader(
            IBatchScheduler batchScheduler,
            IMediator mediator)
            : base(batchScheduler)
        {
            _mediator = mediator;
        }

        protected override async Task<IReadOnlyDictionary<string, Patient>> LoadBatchAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken)
        {
            var queries = ConvertData(keys);
            GraphQlResponse response = await _mediator.Send(new GraphQlRequest("Patient", queries), CancellationToken.None);

            var patients = response.Patients;

            Dictionary<string, Patient> dict = new Dictionary<string, Patient>();

            var index = 0;
            foreach (var patient in patients)
            {
                var patientElement = patient.ToPoco<Patient>();
                dict.Add(patientElement.Id, patientElement);
                index++;
            }

            return dict;
        }

        private static List<Tuple<string, string>> ConvertData(IReadOnlyList<string> ids)
        {
            var queries = new List<Tuple<string, string>>();
            var concatenatedIds = string.Empty;
            int totalIds = ids.Count;

            for (int i = 0; i < totalIds; i++)
            {
                concatenatedIds += ids[i];
                if (i != totalIds - 1)
                {
                    concatenatedIds += ",";
                }
            }

            queries.Add(Tuple.Create("_id", concatenatedIds));
            return queries;
        }
    }
}