﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Security.Claims;
using FakeItEasy;
using NodaTime;
using Squidex.Domain.Apps.Core.Contents;
using Squidex.Domain.Apps.Core.HandleRules;
using Squidex.Domain.Apps.Core.HandleRules.EnrichedEvents;
using Squidex.Domain.Apps.Core.Scripting;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Json.Objects;
using Squidex.Shared.Identity;
using Squidex.Shared.Users;
using Xunit;

namespace Squidex.Domain.Apps.Core.Operations.HandleRules
{
    public class RuleEventFormatterTests
    {
        private readonly IUser user = A.Fake<IUser>();
        private readonly IRuleUrlGenerator urlGenerator = A.Fake<IRuleUrlGenerator>();
        private readonly NamedId<Guid> appId = NamedId.Of(Guid.NewGuid(), "my-app");
        private readonly NamedId<Guid> schemaId = NamedId.Of(Guid.NewGuid(), "my-schema");
        private readonly Guid contentId = Guid.NewGuid();
        private readonly RuleEventFormatter sut;

        public RuleEventFormatterTests()
        {
            A.CallTo(() => user.Email)
                .Returns("me@email.com");

            A.CallTo(() => user.Claims)
                .Returns(new List<Claim> { new Claim(SquidexClaimTypes.DisplayName, "me") });

            A.CallTo(() => urlGenerator.GenerateContentUIUrl(appId, schemaId, contentId))
                .Returns("content-url");

            sut = new RuleEventFormatter(TestUtils.DefaultSerializer, urlGenerator, new JintScriptEngine());
        }

        [Fact]
        public void Should_serialize_object_to_json()
        {
            var result = sut.ToPayload(new { Value = 1 });

            Assert.True(result is string);
        }

        [Fact]
        public void Should_create_payload()
        {
            var @event = new EnrichedContentEvent { AppId = appId };

            var result = sut.ToPayload(@event);

            Assert.True(result is string);
        }

        [Fact]
        public void Should_create_envelope_data_from_event()
        {
            var @event = new EnrichedContentEvent { AppId = appId, Name = "MyEventName" };

            var result = sut.ToEnvelope(@event);

            Assert.Contains("MyEventName", result);
        }

        [Theory]
        [InlineData("Name $APP_NAME has id $APP_ID")]
        [InlineData("Script(`Name ${event.appId.name} has id ${event.appId.id}`)")]
        public void Should_replace_app_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { AppId = appId };

            var result = sut.Format(script, @event);

            Assert.Equal($"Name my-app has id {appId.Id}", result);
        }

        [Theory]
        [InlineData("Name $SCHEMA_NAME has id $SCHEMA_ID")]
        [InlineData("Script(`Name ${event.schemaId.name} has id ${event.schemaId.id}`)")]
        public void Should_replace_schema_information_from_event(string script)
        {
            var @event = new EnrichedContentEvent { SchemaId = schemaId };

            var result = sut.Format(script, @event);

            Assert.Equal($"Name my-schema has id {schemaId.Id}", result);
        }

        [Theory]
        [InlineData("Date: $TIMESTAMP_DATE, Full: $TIMESTAMP_DATETIME")]
        [InlineData("Script(`Date: ${formatDate(event.timestamp, 'yyyy-MM-dd')}, Full: ${formatDate(event.timestamp, 'yyyy-MM-dd-hh-mm-ss')}`)")]
        public void Should_replace_timestamp_information_from_event(string script)
        {
            var now = DateTime.UtcNow;

            var envelope = new EnrichedContentEvent { Timestamp = Instant.FromDateTimeUtc(now) };

            var result = sut.Format(script, envelope);

            Assert.Equal($"Date: {now:yyyy-MM-dd}, Full: {now:yyyy-MM-dd-hh-mm-ss}", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email})`)")]
        public void Should_format_email_and_display_name_from_user(string script)
        {
            var @event = new EnrichedContentEvent { User = user, Actor = new RefToken(RefTokenType.Subject, "123") };

            var result = sut.Format(script, @event);

            Assert.Equal("From me (me@email.com)", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email})`)")]
        public void Should_return_null_if_user_is_not_found(string script)
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Subject, "123") };

            var result = sut.Format(script, @event);

            Assert.Equal("From null (null)", result);
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL)")]
        [InlineData("Script(`From ${event.user.name} (${event.user.email})`)", Skip = "Not Supported")]
        public void Should_return_null_if_user_is_not_found_with_scripting(string script)
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Subject, "123") };

            var result = sut.Format(script, @event);

            Assert.Equal("From null (null)", result);
        }

        [Theory]
        [InlineData("Go to $CONTENT_URL")]
        [InlineData("Script(`Go to ${contentUrl()}`)")]
        public void Should_replace_content_url_from_event(string script)
        {
            var @event = new EnrichedContentEvent { AppId = appId, Id = contentId, SchemaId = schemaId };

            var result = sut.Format(script, @event);

            Assert.Equal("Go to content-url", result);
        }

        [Theory]
        [InlineData("Go to $CONTENT_URL")]
        [InlineData("Script(`Go to ${contentUrl()}`)")]
        public void Should_format_content_url_when_not_found(string script)
        {
            Assert.Equal("Go to null", sut.Format(script, new EnrichedAssetEvent()));
        }

        [Theory]
        [InlineData("$CONTENT_ACTION")]
        [InlineData("Script(`${event.type}`)")]
        public void Should_format_content_actions_when_found(string script)
        {
            Assert.Equal("Created", sut.Format(script, new EnrichedContentEvent { Type = EnrichedContentEventType.Created }));
        }

        [Theory]
        [InlineData("$CONTENT_DATA.country.iv")]
        [InlineData("Script(`${event.data.country.iv}`)")]
        public void Should_return_null_when_field_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.de")]
        [InlineData("Script(`${event.data.country.iv}`)")]
        public void Should_return_null_when_partition_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.10")]
        [InlineData("Script(`${event.data.country.de[10]}`)")]
        public void Should_return_null_when_array_item_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", JsonValue.Array()))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.de.Name")]
        [InlineData("Script(`${event.data.city.de.Location}`)")]
        public void Should_return_null_when_property_not_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("null", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv")]
        [InlineData("Script(`${event.data.city.iv}`)")]
        public void Should_return_plain_value_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", "Berlin"))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.0")]
        [InlineData("Script(`${event.data.city.iv[0]}`)")]
        public void Should_return_plain_value_from_array_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", JsonValue.Array(
                                    "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv.name")]
        [InlineData("Script(`${event.data.city.iv.name}`)")]
        public void Should_return_plain_value_from_object_when_found(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("Berlin", result);
        }

        [Theory]
        [InlineData("$CONTENT_DATA.city.iv")]
        [InlineData("Script(`${event.data.city.iv}`)", Skip = "Not Supported")]
        public void Should_return_json_string_when_object(string script)
        {
            var @event = new EnrichedContentEvent
            {
                Data =
                    new NamedContentData()
                        .AddField("city",
                            new ContentFieldData()
                                .AddValue("iv", JsonValue.Object().Add("name", "Berlin")))
            };

            var result = sut.Format(script, @event);

            Assert.Equal("{\"name\":\"Berlin\"}", result);
        }

        [Theory]
        [InlineData("$CONTENT_ACTION")]
        public void Should_null_when_content_action_not_found(string script)
        {
            Assert.Equal("null", sut.Format(script, new EnrichedAssetEvent()));
        }

        [Theory]
        [InlineData("From $USER_NAME ($USER_EMAIL)")]
        public void Should_format_email_and_display_name_from_client(string script)
        {
            var @event = new EnrichedContentEvent { Actor = new RefToken(RefTokenType.Client, "android") };

            var result = sut.Format(script, @event);

            Assert.Equal("From client:android (client:android)", result);
        }
    }
}
