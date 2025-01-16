﻿using PaperlessServices.AutoMapper;

namespace PaperlessServices.Extensions;

public static class AutoMapperModule
{
    public static void AddAutoMapperProfiles(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(AutoMapperConfig).Assembly);
    }
}
