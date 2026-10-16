using Application.Interface.IService;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.Service
{
    public class PasswordHasherService : IPasswordHasher
    {
        private readonly PasswordHasher<User> passwordHasher;

        public PasswordHasherService()
        {
            passwordHasher = new PasswordHasher<User>();
        }

        public string HashPassword(string password)
        {
            return passwordHasher.HashPassword(null!, password);
        }

        public bool VerifyPassword(string hashedPassword, string providedPassword)
        {
            var result = passwordHasher.VerifyHashedPassword(null!, hashedPassword, providedPassword);
            return result != PasswordVerificationResult.Failed;
        }
    }
}