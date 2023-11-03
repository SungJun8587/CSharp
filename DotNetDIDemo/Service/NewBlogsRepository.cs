using DotNetDIDemo.Model;

namespace DotNetDIDemo.Service
{
    public class NewBlogRepository : IBlogRepository
    {
        public static List<Blog> blogs = new()
        {
            new Blog() { Id = 1000, Title = "DI Deep Dive", Content = "This is a five part series" },
            new Blog() { Id = 2000, Title = "Minimal API", Content = "Learn in 30 mins" },
            new Blog() { Id = 3000, Title = "Prescription API", Content = "Still under construction" }
        };

        public List<Blog> GetAllBlogs()
        {
            return blogs;
        }
    }
}