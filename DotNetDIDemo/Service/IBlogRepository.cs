using DotNetDIDemo.Model;

namespace DotNetDIDemo.Service
{
    public interface IBlogRepository
    {
        List<Blog> GetAllBlogs();
    }
}